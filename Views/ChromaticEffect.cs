using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

/// <summary>
/// iOS 26-style colour split: red and blue channels slide apart for a split second, like a misaligned print.
/// The pixel shader is compiled at start-up with Windows' own d3dcompiler_47.dll, so no build tools are needed.
/// </summary>
public sealed class ChromaticEffect : ShaderEffect
{
    private const string Hlsl = @"
sampler2D input : register(s0);
float amount : register(c0);
float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 d = float2(amount, amount * 0.25);
    float4 c = tex2D(input, uv);
    float4 r = tex2D(input, uv + d);
    float4 b = tex2D(input, uv - d);
    float a = max(c.a, max(r.a, b.a));
    return float4(r.r, c.g, b.b, a);
}";

    private static PixelShader? _shader;
    private static bool _tried;

    public static readonly DependencyProperty InputProperty =
        RegisterPixelShaderSamplerProperty("Input", typeof(ChromaticEffect), 0);

    /// <summary>Offset of the red / blue channels, in fractions of the element width.</summary>
    public static readonly DependencyProperty AmountProperty =
        DependencyProperty.Register(nameof(Amount), typeof(double), typeof(ChromaticEffect),
            new UIPropertyMetadata(0.0, PixelShaderConstantCallback(0)));

    public Brush Input
    {
        get => (Brush)GetValue(InputProperty);
        set => SetValue(InputProperty, value);
    }

    public double Amount
    {
        get => (double)GetValue(AmountProperty);
        set => SetValue(AmountProperty, value);
    }

    private ChromaticEffect(PixelShader shader)
    {
        PixelShader = shader;
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(AmountProperty);
        // the shader reads a little outside the element: give it room so the fringes aren't cut off
        PaddingLeft = PaddingRight = 12;
        PaddingTop = PaddingBottom = 4;
    }

    /// <summary>null when the shader can't be compiled on this machine (the effect is then simply skipped).</summary>
    public static ChromaticEffect? TryCreate()
    {
        if (!_tried)
        {
            _tried = true;
            try
            {
                var bytes = Compile(Hlsl, "main", "ps_2_0");
                var ps = new PixelShader();
                ps.SetStreamSource(new MemoryStream(bytes));
                _shader = ps;
            }
            catch (Exception e)
            {
                Log.Write("chromatic shader: " + e.Message);
            }
        }
        return _shader == null ? null : new ChromaticEffect(_shader);
    }

    // ------------------------------------------------------------------ D3DCompile

    [ComImport, Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ID3DBlob
    {
        [PreserveSig] IntPtr GetBufferPointer();
        [PreserveSig] IntPtr GetBufferSize();
    }

    [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int D3DCompile(
        byte[] srcData, IntPtr srcDataSize,
        [MarshalAs(UnmanagedType.LPStr)] string? sourceName,
        IntPtr defines, IntPtr include,
        [MarshalAs(UnmanagedType.LPStr)] string entryPoint,
        [MarshalAs(UnmanagedType.LPStr)] string target,
        uint flags1, uint flags2,
        out ID3DBlob? code, out ID3DBlob? errors);

    private static byte[] Compile(string source, string entry, string target)
    {
        var src = Encoding.ASCII.GetBytes(source);
        const uint OptimizationLevel3 = 1 << 15;
        int hr = D3DCompile(src, (IntPtr)src.Length, "chromatic", IntPtr.Zero, IntPtr.Zero, entry, target,
            OptimizationLevel3, 0, out var code, out var errors);
        if (hr < 0 || code == null)
        {
            var msg = errors != null ? Marshal.PtrToStringAnsi(errors.GetBufferPointer(), (int)errors.GetBufferSize()) : $"hr=0x{hr:X8}";
            throw new InvalidOperationException(msg);
        }
        var bytes = new byte[(int)code.GetBufferSize()];
        Marshal.Copy(code.GetBufferPointer(), bytes, 0, bytes.Length);
        return bytes;
    }
}
