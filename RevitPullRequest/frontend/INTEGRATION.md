# Frontend-Backend Integration Guide

## Message Flow

### Frontend → Backend (Outgoing Messages)

The frontend sends messages using `window.chrome.webview.postMessage(messageObject)`:
- WebView2 automatically serializes the object to JSON
- Backend receives via `WebMessageReceived` event
- Backend deserializes JSON string to specific message types

**Message Types:**
- `record:elements` - Record selected elements
- `compare:elements` - Compare elements
- `clear:visualization` - Clear visualization
- `get:selection` - Request current selection

### Backend → Frontend (Incoming Messages)

The backend sends messages using `PostWebMessageAsJson(jsonString)`:
- Backend serializes message object to JSON string
- Frontend receives via `addEventListener("message", ...)`
- Frontend parses JSON string from `event.data`

**Message Types:**
- `selection:changed` - Selection has changed
- `record:complete` - Recording operation completed
- `compare:complete` - Comparison operation completed

## Message Format Compatibility

### TypeScript Interfaces (Frontend)
Located in `frontend/src/types/messages.ts`

### C# Models (Backend)
Located in `revit_backend/Models/WebMessage.cs`

Both use the same JSON property names:
- `type` (lowercase) - Message type identifier
- `elements` - Array of ElementInfo objects
- `success` - Boolean indicating success
- `results` - Array of result objects
- `error` - Error message string

## Testing

1. **Start Frontend**: `npm run dev` in `frontend/` directory
2. **Start Revit Plugin**: Use VS Code debug configuration
3. **Verify Communication**:
   - Select elements in Revit → Should see selection in frontend
   - Click "Record" → Should see status message
   - Click "Compare" → Should see comparison results

## Troubleshooting

- **No messages received**: Check browser console and Revit log file (`DUMP/logs/`)
- **Message parsing errors**: Verify JSON format matches TypeScript/C# models
- **WebView not available**: Ensure running inside Revit WebView2, not standalone browser

