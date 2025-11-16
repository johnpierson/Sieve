using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;
using PullRequestForRevit.Models;

namespace PullRequestForRevit.Services;

// Runtime proxy using Reflection.Emit to work around ExternalServiceId interface issue
// Creates a type at runtime that properly implements IDirectContext3DServer
// This bypasses the compile-time type checking that prevents us from implementing the interface
public static class DirectContext3DProxyFactory
{
    private static Type? _proxyType;
    private static readonly object _lock = new object();

    public static IDirectContext3DServer CreateProxy(
        Dictionary<string, ElementData> recordedData,
        Dictionary<string, Element> currentElements,
        Document document)
    {
        lock (_lock)
        {
            if (_proxyType == null)
            {
                _proxyType = CreateProxyType();
            }

            var implementation = new ComparisonDirectContext3D(recordedData, currentElements, document);
            return (IDirectContext3DServer)Activator.CreateInstance(_proxyType, implementation)!;
        }
    }

    private static Type CreateProxyType()
    {
        var assemblyName = new AssemblyName("DirectContext3DProxyAssembly");
        var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        var moduleBuilder = assemblyBuilder.DefineDynamicModule("DirectContext3DProxyModule");
        var typeBuilder = moduleBuilder.DefineType(
            "DirectContext3DProxy",
            TypeAttributes.Public | TypeAttributes.Class,
            null,
            new[] { typeof(IDirectContext3DServer) });

        var implementationField = typeBuilder.DefineField(
            "_implementation",
            typeof(ComparisonDirectContext3D),
            FieldAttributes.Private | FieldAttributes.InitOnly);

        // Constructor
        var constructorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            new[] { typeof(ComparisonDirectContext3D) });

        var constructorIl = constructorBuilder.GetILGenerator();
        constructorIl.Emit(OpCodes.Ldarg_0);
        constructorIl.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        constructorIl.Emit(OpCodes.Ldarg_0);
        constructorIl.Emit(OpCodes.Ldarg_1);
        constructorIl.Emit(OpCodes.Stfld, implementationField);
        constructorIl.Emit(OpCodes.Ret);

        // Implement all interface methods by delegating to implementation
        ImplementInterfaceMethods(typeBuilder, implementationField);

        return typeBuilder.CreateType()!;
    }

    private static void ImplementInterfaceMethods(TypeBuilder typeBuilder, FieldBuilder implementationField)
    {
        var interfaceType = typeof(IDirectContext3DServer);
        var baseInterface = typeof(IExternalServer);

        // Implement IExternalServer methods (signatures taken from the interface itself)
        ImplementMethod(typeBuilder, implementationField, baseInterface, "GetServerId", typeof(Guid));
        ImplementGetServiceId(typeBuilder, implementationField);
        ImplementMethod(typeBuilder, implementationField, baseInterface, "GetVendorId", typeof(string));
        ImplementMethod(typeBuilder, implementationField, baseInterface, "GetName", typeof(string));
        ImplementMethod(typeBuilder, implementationField, baseInterface, "GetDescription", typeof(string));

        // Implement IDirectContext3DServer methods
        ImplementMethod(typeBuilder, implementationField, interfaceType, "GetSourceId", typeof(string));
        ImplementMethod(typeBuilder, implementationField, interfaceType, "GetApplicationId", typeof(string));
        ImplementMethod(typeBuilder, implementationField, interfaceType, "UsesHandles", typeof(bool));
        ImplementMethod(typeBuilder, implementationField, interfaceType, "CanExecute", typeof(bool), typeof(View));
        ImplementMethod(typeBuilder, implementationField, interfaceType, "GetBoundingBox", typeof(Outline), typeof(View));
        ImplementMethod(typeBuilder, implementationField, interfaceType, "RenderScene", typeof(void), typeof(View), typeof(DisplayStyle));
        ImplementMethod(typeBuilder, implementationField, interfaceType, "UseInTransparentPass", typeof(bool), typeof(View));
    }

    private static void ImplementMethod(
        TypeBuilder typeBuilder,
        FieldBuilder implementationField,
        Type interfaceType,
        string methodName,
        Type returnType,
        params Type[] parameterTypes)
    {
        var interfaceMethod = interfaceType.GetMethod(methodName, parameterTypes);
        if (interfaceMethod == null) return;

        var methodBuilder = typeBuilder.DefineMethod(
            interfaceType.Name + "." + methodName,
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            interfaceMethod.ReturnType,
            parameterTypes);

        typeBuilder.DefineMethodOverride(methodBuilder, interfaceMethod);

        var il = methodBuilder.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, implementationField);

        // Load arguments (skip 'this')
        for (int i = 0; i < parameterTypes.Length; i++)
        {
            il.Emit(OpCodes.Ldarg, i + 1);
        }

        // Call the implementation method
        var implMethod = typeof(ComparisonDirectContext3D).GetMethod(methodName, parameterTypes);
        if (implMethod != null)
        {
            il.Emit(OpCodes.Callvirt, implMethod);
        }
        else
        {
            Logger.Instance.LogError($"Method {methodName} not found on ComparisonDirectContext3D");
            if (returnType == typeof(void))
            {
                il.Emit(OpCodes.Ret);
            }
            else if (interfaceMethod.ReturnType.IsValueType)
            {
                var local = il.DeclareLocal(interfaceMethod.ReturnType);
                il.Emit(OpCodes.Ldloca_S, local);
                il.Emit(OpCodes.Initobj, interfaceMethod.ReturnType);
                il.Emit(OpCodes.Ldloc, local);
            }
            else
            {
                il.Emit(OpCodes.Ldnull);
            }
        }

        il.Emit(OpCodes.Ret);
    }

    private static void ImplementGetServiceId(TypeBuilder typeBuilder, FieldBuilder implementationField)
    {
        // GetServiceId() is special - it needs to return ExternalServiceId (internal type)
        // We'll use reflection to get the correct return type and return the service ID
        var interfaceMethod = typeof(IExternalServer).GetMethod("GetServiceId");
        if (interfaceMethod == null) return;

        var methodBuilder = typeBuilder.DefineMethod(
            "IExternalServer.GetServiceId",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            interfaceMethod.ReturnType, // Use the actual (internal) ExternalServiceId type
            Type.EmptyTypes);

        typeBuilder.DefineMethodOverride(methodBuilder, interfaceMethod);

        var il = methodBuilder.GetILGenerator();
        
        // Get the DirectContext3DService using reflection
        var getServiceIdMethod = typeof(DirectContext3DProxyFactory).GetMethod(
            "GetDirectContext3DService",
            BindingFlags.NonPublic | BindingFlags.Static);
        
        if (getServiceIdMethod != null)
        {
            // Call helper that returns object, then cast to the actual ExternalServiceId type
            il.Emit(OpCodes.Call, getServiceIdMethod);
            il.Emit(OpCodes.Castclass, interfaceMethod.ReturnType);
        }
        else
        {
            // Fallback: return the service directly
            var builtInServicesType = typeof(ExternalServices).GetNestedType("BuiltInExternalServices", BindingFlags.Public | BindingFlags.Static);
            if (builtInServicesType != null)
            {
                    var property = builtInServicesType.GetProperty("DirectContext3DService", BindingFlags.Public | BindingFlags.Static);
                if (property != null)
                {
                    il.Emit(OpCodes.Call, property.GetMethod!);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
            }
            else
            {
                il.Emit(OpCodes.Ldnull);
            }
        }

        il.Emit(OpCodes.Ret);
    }

    private static object GetDirectContext3DService()
    {
        try
        {
            return ExternalServices.BuiltInExternalServices.DirectContext3DService;
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError("Error getting DirectContext3DService", ex);
            throw;
        }
    }
}

