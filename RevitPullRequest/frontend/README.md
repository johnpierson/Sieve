# PullRequest-For-Revit Frontend

Frontend for the PullRequest-For-Revit plugin, built with React, TypeScript, and Vite.

## Features

- **Record Elements**: Record geometry and metadata of selected Revit elements
- **Compare Elements**: Compare recorded elements with current state and visualize changes
- **Real-time Selection**: Automatically updates when selection changes in Revit
- **WebView2 Integration**: Uses messaging pattern to communicate with Revit backend

## Development

```bash
# Install dependencies
npm install

# Start development server
npm run dev

# Build for production
npm run build
```

The frontend runs on port 8001 and communicates with the Revit backend via WebView2 messaging.

