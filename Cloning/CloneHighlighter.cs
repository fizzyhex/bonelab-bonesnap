using BoneSnap.Personalisation;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoneSnap.Cloning;

/// <summary>
/// Draws a box around a clone, e.g. while its deletion is awaiting confirmation.
/// </summary>
internal static class CloneHighlighter
{
    private static AvatarClone? _highlighted;
    private static Material? _lineMaterial;
    private static bool _hooked;

    public static void Show(AvatarClone clone)
    {
        _highlighted = clone;

        if (_hooked)
            return;

        _hooked = true;
        RenderPipelineManager.add_endCameraRendering(
            (Il2CppSystem.Action<ScriptableRenderContext, Camera>)OnEndCameraRendering);
    }

    public static void Hide(AvatarClone clone)
    {
        if (_highlighted == clone)
            _highlighted = null;
    }

    private static void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        // Captures render into textures, so the box stays out of snaps
        if (_highlighted == null || _highlighted.Root == null || camera.targetTexture != null)
            return;

        var material = GetLineMaterial();

        if (material == null)
            return;

        var bounds = _highlighted.GetBounds();
        var min = bounds.min;
        var max = bounds.max;

        material.SetPass(0);
        GL.PushMatrix();
        GL.LoadProjectionMatrix(camera.projectionMatrix);
        GL.modelview = camera.worldToCameraMatrix;
        GL.Begin(GL.LINES);
        GL.Color(AccentColor.Current);

        for (var corner = 0; corner < 8; corner++)
        {
            // Join corners
            for (var axis = 0; axis < 3; axis++)
            {
                if ((corner & (1 << axis)) != 0)
                    continue;

                GL.Vertex(Corner(corner));
                GL.Vertex(Corner(corner | (1 << axis)));
            }
        }

        GL.End();
        GL.PopMatrix();
        return;

        Vector3 Corner(int index) => new(
            (index & 1) == 0 ? min.x : max.x,
            (index & 2) == 0 ? min.y : max.y,
            (index & 4) == 0 ? min.z : max.z);
    }

    private static Material? GetLineMaterial()
    {
        if (_lineMaterial != null)
            return _lineMaterial;

        var shader = Shader.Find("Hidden/Internal-Colored");

        if (shader == null)
            return null;

        _lineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        _lineMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
        _lineMaterial.SetInt("_Cull", (int)CullMode.Off);
        _lineMaterial.SetInt("_ZWrite", 0);
        return _lineMaterial;
    }
}
