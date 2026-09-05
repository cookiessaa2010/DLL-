# Bannerlord Shader Cache Redirector

A small, source-visible patcher for **Mount & Blade II: Bannerlord** that moves the game's native local shader cache away from `C:\ProgramData` to a short path on another drive.

This project exists for players whose system SSD is being filled by Bannerlord shader compilation, especially large mod setups.

## Supported game build

**Mount & Blade II: Bannerlord v1.3.15.110062 only**

The patcher verifies the exact original `TaleWorlds.Native.dll` before modifying anything:

```text
SHA-256: 9589f5b59c9649461817ac04620e942303590ce93d0b643d17cababd8f581bd3
```

If the DLL does not match, the patcher stops without changing it.

## What it changes

Default Bannerlord path:

```text
C:\ProgramData\Mount and Blade II Bannerlord\Shaders\
```

Default redirect preset in this project:

```text
D:\MNB\Shaders\
```

The redirect is performed by patching only the native shader-path construction code in `TaleWorlds.Native.dll`. It does **not** use `mklink`, junctions, or symbolic links.

The original DLL is backed up automatically as:

```text
TaleWorlds.Native.dll.KaiOriginal.bak
```

`RESTORE.bat` restores that verified original backup.

## Installation

1. Close Bannerlord and its launcher.
2. Download the project ZIP or the packaged ZIP from `dist/`.
3. Put these files into:

```text
Mount & Blade II Bannerlord\bin\Win64_Shipping_Client\
```

Files:

```text
PATCH.bat
RESTORE.bat
KaiShaderPatch.ps1
ShaderRedirector.ini
```

4. Open `ShaderRedirector.ini` and set the path you want.
5. Run `PATCH.bat`.
6. Start Bannerlord and check that shader files are being created in the new directory.

If Windows denies write access to the game folder, run `PATCH.bat` as Administrator.

## Target-path limitation

For this exact Bannerlord build, the conservative patch uses an existing **16-byte native literal slot**. Therefore the replacement path must:

- be a local drive path such as `D:\Shaders`
- use ASCII characters only
- be **15 bytes or fewer including the final `\`**

The patcher adds the final backslash automatically.

Examples that fit:

```text
D:\Shaders
E:\BL\Shaders
D:\MNB\Shaders
```

A longer path is rejected before the DLL is modified.

## Changing the target later

Edit `ShaderRedirector.ini` and run `PATCH.bat` again. If the DLL contains a recognized patch from this tool and the verified original backup is present, the redirect target can be updated safely.

## Restore / uninstall

Run:

```text
RESTORE.bat
```

The tool restores the backup only if its SHA-256 matches the supported original build. It also refuses to overwrite a DLL that looks like a game update or an unrelated modification.

## Important warnings

- **Do not use this on another Bannerlord version.**
- A Steam file verification or game update may restore the original DLL.
- Patching a native game DLL invalidates the original file signature.
- For multiplayer, anti-cheat environments, game updates, or troubleshooting, restore the original DLL first.
- The patch does not delete old shader caches. Remove old cache folders manually only after confirming the new location works.
- No TaleWorlds DLL is distributed in this repository. The patcher modifies the user's own installed file locally.

## Technical notes

The supported build constructs the shader-cache path inside `TaleWorlds.Native.dll`. This patch removes the `CommonAppData` + product-name + `Shaders` concatenation sequence and points the native string construction at a replacement short path stored in an existing literal slot.

The patch is intentionally build-locked and uses:

- exact SHA-256 validation for the original DLL
- exact byte-signature checks before first patching
- automatic verified backup
- recognition of its own patched byte layout for safe target changes
- post-write signature and path verification

The known tested default (`D:\MNB\Shaders\`) produces:

```text
SHA-256: 7fcf4cf7c7afdaff1586c40378daf691358cd25a29a8b17053ed40116beee8e3
```

## License

The patcher source in this repository is released under the MIT License. Mount & Blade II: Bannerlord and TaleWorlds binaries are property of their respective owners and are not included.
