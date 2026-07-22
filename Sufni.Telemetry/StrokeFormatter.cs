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
            var stroke = new Stroke();
            var fieldCount = reader.ReadMapHeader();
            for (var fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
            {
                var fieldName = reader.ReadString();
                switch (fieldName)
                {
                    case nameof(Stroke.Start):
                        stroke.Start = reader.ReadInt32();
                        break;
                    case nameof(Stroke.End):
                        stroke.End = reader.ReadInt32();
                        break;
                    case nameof(Stroke.Stat):
                        stroke.Stat = options.Resolver.GetFormatterWithVerify<StrokeStat>()
                            .Deserialize(ref reader, options)!;
                        break;
                    case nameof(Stroke.DigitizedTravel):
                        stroke.DigitizedTravel = options.Resolver.GetFormatterWithVerify<int[]>()
                            .Deserialize(ref reader, options)!;
                        break;
                    case nameof(Stroke.DigitizedVelocity):
                        stroke.DigitizedVelocity = options.Resolver.GetFormatterWithVerify<int[]>()
                            .Deserialize(ref reader, options)!;
                        break;
                    case nameof(Stroke.FineDigitizedVelocity):
                        stroke.FineDigitizedVelocity = options.Resolver.GetFormatterWithVerify<int[]>()
                            .Deserialize(ref reader, options)!;
                        break;
                    case nameof(Stroke.StartSeconds):
                        stroke.StartSeconds = reader.ReadDouble();
                        break;
                    case nameof(Stroke.EndSeconds):
                        stroke.EndSeconds = reader.ReadDouble();
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            return stroke;
        }
        finally
        {
            reader.Depth--;
        }
    }
}
