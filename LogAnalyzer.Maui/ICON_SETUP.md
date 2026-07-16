# Setting Up Custom Icon for LogAnalyzer.Maui

## ✅ Project Configuration Updated

The project file has been configured to use `log_15199.png` as the application icon.

## 📋 Next Steps

### Option 1: Using the PowerShell Script (Recommended)

Run the following command from the project root:

```powershell
.\copy-icon.ps1 "C:\path\to\your\log_15199.png"
```

Replace `C:\path\to\your\log_15199.png` with the actual path to your icon file.

### Option 2: Manual Copy

1. Navigate to your project folder:
   ```
   C:\repo\POC\AlyCE_LogAnalyzer\LogAnalyzer.Maui\
   ```

2. Copy your `log_15199.png` file to:
   ```
   Resources\AppIcon\log_15199.png
   ```

## 🔨 Build and Apply

After copying the file:

1. **Clean the solution:**
   ```powershell
   dotnet clean
   ```

2. **Rebuild the project:**
   ```powershell
   dotnet build
   ```

3. The new icon will be applied to:
   - Windows executable (.exe)
   - Application tiles
   - Taskbar icon
   - Window title bar icon

## 📐 Icon Requirements

For best results, your `log_15199.png` should be:
- **Size:** At least 256x256 pixels (512x512 or 1024x1024 recommended)
- **Format:** PNG with transparency support
- **Aspect ratio:** Square (1:1)

MAUI's image resizer will automatically generate all required sizes for Windows.

## ✨ What's Changed

**Modified file:** `LogAnalyzer.Maui.csproj`
- Changed from: `<MauiIcon Include="Resources\AppIcon\appicon.svg" ... />`
- Changed to: `<MauiIcon Include="Resources\AppIcon\log_15199.png" />`

## 🎯 Result

After rebuilding, your application will display the custom log icon instead of the default .NET MAUI icon.

## 🔍 Verify Installation

Run this command to check if the file is in place:

```powershell
Test-Path "Resources\AppIcon\log_15199.png"
```

If it returns `True`, you're ready to build!
