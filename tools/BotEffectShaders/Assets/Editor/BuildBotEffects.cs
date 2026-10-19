using System;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

public static class BuildBotEffects
{
    public static void Run()
    {
        try
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/ArenaBotEffects.shader");
            if (shader == null) throw new Exception("shader asset missing");
            foreach (var error in ShaderUtil.GetShaderMessages(shader))
                if (error.severity == ShaderCompilerMessageSeverity.Error) throw new Exception(error.message);
            var output = Environment.GetEnvironmentVariable("ARENA_EFFECT_SHADER_OUTPUT");
            if (string.IsNullOrEmpty(output)) throw new Exception("ARENA_EFFECT_SHADER_OUTPUT missing");
            Directory.CreateDirectory(output);
            var manifest = BuildPipeline.BuildAssetBundles(output, new[] { new AssetBundleBuild {
                assetBundleName = "bot-effect-outlines.bundle", assetNames = new[] { "Assets/ArenaBotEffects.shader" }
            } }, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode, BuildTarget.StandaloneWindows64);
            if (manifest == null) throw new Exception("shader bundle build failed");
            foreach (var error in ShaderUtil.GetShaderMessages(shader))
                if (error.severity == ShaderCompilerMessageSeverity.Error) throw new Exception(error.message);
            Debug.Log("ARENA_BOT_EFFECT_SHADER_BUILD_PASSED: " + Application.unityVersion);
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
}
