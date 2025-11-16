# Grok API Setup

This application uses xAI's Grok-4-fast-non-reasoning model for natural language processing of watchlist entries.

## Setup Instructions

### 1. Get an xAI API Key

1. Visit [xAI's website](https://x.ai) and sign up for an account
2. Navigate to the API section to generate an API key
3. Copy your API key

### 2. Configure the API Key

You can set the API key in one of the following ways:

#### Option A: Environment Variable (Recommended)
Set one of these environment variables:
- `XAI_API_KEY` 
- `GROK_API_KEY`

**Windows (PowerShell):**
```powershell
$env:XAI_API_KEY = "your-api-key-here"
```

**Windows (Command Prompt):**
```cmd
set XAI_API_KEY=your-api-key-here
```

**Windows (System-wide):**
1. Open System Properties → Environment Variables
2. Add a new User variable:
   - Name: `XAI_API_KEY`
   - Value: `your-api-key-here`

#### Option B: Programmatic Configuration
You can also pass the API key when creating the `NaturalLanguageParser`:

```csharp
var parser = new NaturalLanguageParser("your-api-key-here");
```

### 3. Verify Setup

When you open the Watchlist window and enter natural language, you should see:
- Status message: "Processing with Grok AI..." when parsing
- More accurate extraction of categories, parameters, and values

### 4. Fallback Behavior

If the API key is not configured or the API call fails, the system will automatically fall back to a simple parser that extracts basic element names.

## API Costs

Grok-4-fast-non-reasoning pricing:
- Input tokens: $0.20 per million
- Output tokens: $0.50 per million

Typical watchlist parsing uses minimal tokens, making it very cost-effective.

## Troubleshooting

- **"No items could be parsed"**: Check that your API key is set correctly
- **API errors**: Verify your API key is valid and you have sufficient credits
- **Slow responses**: Check your internet connection; API calls require network access

