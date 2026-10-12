using PDFtoImage;
using SkiaSharp;

namespace Dusiburg.AI.Trasporto.Eval.Cmr;

/// <summary>
/// Rende un CMR sintetico come una scansione "sporca": pagina a bassa risoluzione, leggermente ruotata, sfocata, con rumore e
/// compressione JPEG. Serve solo al confronto dei modelli (PLAN §5, Fase 9): l'OCR non è una funzione del prodotto, e un PDF
/// senza testo resta rifiutato dai servizi.
/// </summary>
public static class CmrImageRenderer
{
    /// <summary>Sottocartella delle immagini, sotto quella dei PDF del golden set.</summary>
    public const string Directory = "images";

    public const string Extension = ".jpg";

    public const string MediaType = "image/jpeg";

    /// <summary>I documenti resi come immagine: un caso pulito per impaginazione, merci pericolose, molte righe, note a mano.</summary>
    public static readonly IReadOnlyList<string> Documents = ["CMR-1001", "CMR-1002", "CMR-1003", "CMR-1004", "G-2001", "G-2028"];

    private const int Dpi = 120;
    private const float RotationDegrees = 1.3f;
    private const float BlurSigma = 0.55f;
    private const int NoiseAmplitude = 16;
    private const int JpegQuality = 45;

    public static string FileOf(string code) => $"{GoldenCmrs.GeneratedDirectory}/{Directory}/{code}{Extension}";

    /// <param name="noiseSeed">Seme del rumore: fisso per documento, così la stessa pagina dà la stessa immagine.</param>
    public static byte[] Render(byte[] pdf, int noiseSeed)
    {
#pragma warning disable CA1416 // PDFtoImage dichiara le piattaforme supportate: il tool gira su Windows, come la CI.
        using SKBitmap page = Conversion.ToImage(pdf, options: new RenderOptions(Dpi: Dpi, BackgroundColor: SKColors.White));
#pragma warning restore CA1416
        using var scanned = new SKBitmap(page.Width, page.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);

        using (var canvas = new SKCanvas(scanned))
        using (var paint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(BlurSigma, BlurSigma) })
        {
            // Il piano dello scanner non è bianco, e il foglio non è mai dritto.
            canvas.Clear(new SKColor(236, 234, 228));
            canvas.RotateDegrees(RotationDegrees, page.Width / 2f, page.Height / 2f);
            using SKImage image = SKImage.FromBitmap(page);

            canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear), paint);
        }

        AddNoise(scanned, noiseSeed);

        using SKData encoded = scanned.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);

        return encoded.ToArray();
    }

    private static void AddNoise(SKBitmap bitmap, int seed)
    {
        var random = new Random(seed);
        SKColor[] pixels = bitmap.Pixels;

        for (int index = 0; index < pixels.Length; index++)
        {
            int shift = random.Next(-NoiseAmplitude, NoiseAmplitude + 1);
            SKColor pixel = pixels[index];

            pixels[index] = new SKColor(Clamp(pixel.Red + shift), Clamp(pixel.Green + shift), Clamp(pixel.Blue + shift));
        }

        bitmap.Pixels = pixels;

        static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);
    }
}
