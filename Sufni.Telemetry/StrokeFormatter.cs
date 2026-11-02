using MessagePack;
using MessagePack.Formatters;

namespace Sufni.Telemetry;

public sealed class StrokeFormatter : IMessagePackFormatter<Stroke?>
{
    public void Serialize(
        ref MessagePackWriter writer,
        Stroke? value,
        MessagePackSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNil();
            return;
        }

        writer.WriteMapHeader(5);

        writer.Write(nameof(Stroke.Start));
        writer.Write(value.Start);

        writer.Write(nameof(Stroke.End));
        writer.Write(value.End);

        writer.Write(nameof(Stroke.Stat));
        options.Resolver.GetFormatterWithVerify<StrokeStat>()
            .Serialize(ref writer, value.Stat, options);

        writer.Write(nameof(Stroke.StartSeconds));
        writer.Write(value.StartSeconds);

        writer.Write(nameof(Stroke.EndSeconds));
        writer.Write(value.EndSeconds);
    }

    public Stroke? Deserialize(
        ref MessagePackReader reader,
        MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
        {
            return null;
        }

        options.Security.DepthStep(ref reader);
        try
        {
            var start = 0;
            var end = 0;
            StrokeStat stat = null!;
            int[] digitizedTravel = [], digitizedVelocity = [];
            double startSeconds = 0, endSeconds = 0;
            var fieldCount = reader.ReadMapHeader();
            for (var fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
            {
                var fieldName = reader.ReadString();
                switch (fieldName)
                {
                    case nameof(Stroke.Start):
                        start = reader.ReadInt32();
                        break;
                    case nameof(Stroke.End):
                        end = reader.ReadInt32();
                        break;
                    case nameof(Stroke.Stat):
                        stat = options.Resolver.GetFormatterWithVerify<StrokeStat>()
                            .Deserialize(ref reader, options)!;
                        break;
                    case nameof(Stroke.DigitizedTravel):
                        digitizedTravel = options.Resolver.GetFormatterWithVerify<int[]>()
                            .Deserialize(ref reader, options)!;
                        break;
                    case nameof(Stroke.DigitizedVelocity):
                        digitizedVelocity = options.Resolver.GetFormatterWithVerify<int[]>()
                            .Deserialize(ref reader, options)!;
                        break;
                    case nameof(Stroke.StartSeconds):
                        startSeconds = reader.ReadDouble();
                        break;
                    case nameof(Stroke.EndSeconds):
                        endSeconds = reader.ReadDouble();
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            return new Stroke
            {
                Start = start, End = end, Stat = stat,
                DigitizedTravel = [.. digitizedTravel], DigitizedVelocity = [.. digitizedVelocity],
                StartSeconds = startSeconds, EndSeconds = endSeconds,
            };
        }
        finally
        {
            reader.Depth--;
        }
    }
}
