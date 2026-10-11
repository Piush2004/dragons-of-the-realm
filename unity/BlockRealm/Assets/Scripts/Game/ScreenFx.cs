using UnityEngine;

namespace BlockRealm.Game
{
    /// <summary>
    /// Light-weight post effect for the built-in pipeline: vignette, grade and glow. If the shader is missing or
    /// unsupported the image is simply passed through.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class ScreenFx : MonoBehaviour
    {
        private Material _material;
        private bool _failed;

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (_failed) { Graphics.Blit(source, destination); return; }
            if (_material == null)
            {
                Shader s = Resources.Load<Shader>(GameAssets.Root + "Shaders/ScreenFx");
                if (s == null || !s.isSupported) { _failed = true; Graphics.Blit(source, destination); return; }
                _material = new Material(s);
            }
            _material.SetFloat("_Vignette", 0.42f);
            _material.SetFloat("_Contrast", 1.06f);
            _material.SetFloat("_Saturation", 1.12f);
            _material.SetFloat("_Glow", 0.55f);
            _material.SetColor("_Tint", Color.white);
            Graphics.Blit(source, destination, _material);
        }
    }
}
