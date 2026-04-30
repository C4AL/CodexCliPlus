using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexCliPlus.Core.Constants;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace CodexCliPlus.BuildTool;

public static class MakeMicaVisualStudioCompatibility
{
    private const string OriginalVsWhereArguments = "-latest -property installationPath";
    private const string Vs2026AwareVsWhereArguments =
        "-latest -products * -property installationPath";
    private const string CompatibleExecutableSuffix = ".vs2026.exe";

    public static string TryCreateCompatibleMakeMica(string makeMicaPath, BuildLogger logger)
    {
        var directory = Path.GetDirectoryName(makeMicaPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return makeMicaPath;
        }

        var compatiblePath = Path.Combine(
            directory,
            $"{Path.GetFileNameWithoutExtension(makeMicaPath)}{CompatibleExecutableSuffix}"
        );

        if (IsCompatibleCopyCurrent(makeMicaPath, compatiblePath))
        {
            return compatiblePath;
        }

        var temporaryPath = $"{compatiblePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using var assembly = AssemblyDefinition.ReadAssembly(
                makeMicaPath,
                new ReaderParameters { InMemory = true, ReadingMode = ReadingMode.Immediate }
            );
            var replacementCount = ReplaceVsWhereArguments(assembly.MainModule);
            if (replacementCount == 0)
            {
                TryDeleteFile(temporaryPath);
                return makeMicaPath;
            }

            assembly.Write(temporaryPath);
            File.Move(temporaryPath, compatiblePath, overwrite: true);
            File.SetLastWriteTimeUtc(
                compatiblePath,
                File.GetLastWriteTimeUtc(makeMicaPath).AddSeconds(1)
            );
            logger.Info(
                $"MicaSetup makemica.exe compatibility copy created for VS 2026 Build Tools: {compatiblePath}"
            );
            return compatiblePath;
        }
        catch (BadImageFormatException)
        {
            TryDeleteFile(temporaryPath);
            return makeMicaPath;
        }
        catch (Exception exception)
        {
            TryDeleteFile(temporaryPath);
            logger.Warning(
                $"Could not create VS 2026-compatible makemica.exe copy; using original makemica.exe: {exception.Message}"
            );
            return makeMicaPath;
        }
    }

    private static bool IsCompatibleCopyCurrent(string makeMicaPath, string compatiblePath)
    {
        return File.Exists(compatiblePath)
            && File.GetLastWriteTimeUtc(compatiblePath) > File.GetLastWriteTimeUtc(makeMicaPath);
    }

    private static int ReplaceVsWhereArguments(ModuleDefinition module)
    {
        var replacementCount = 0;
        foreach (var type in EnumerateTypes(module.Types))
        {
            foreach (var method in type.Methods.Where(method => method.HasBody))
            {
                foreach (var instruction in method.Body.Instructions)
                {
                    if (
                        instruction.OpCode == OpCodes.Ldstr
                        && instruction.Operand is string value
                        && string.Equals(value, OriginalVsWhereArguments, StringComparison.Ordinal)
                    )
                    {
                        instruction.Operand = Vs2026AwareVsWhereArguments;
                        replacementCount++;
                    }
                }
            }
        }

        return replacementCount;
    }

    private static IEnumerable<TypeDefinition> EnumerateTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nestedType in EnumerateTypes(type.NestedTypes))
            {
                yield return nestedType;
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch { }
    }
}
