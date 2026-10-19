using System.Text.Json.Serialization;

namespace PivotController.Configuration.Models
{
    /// <summary>Thousands separator for CSV numeric parsing. Values: Comma (,), Dot (.), Space ( ).</summary>
    [JsonConverter(typeof(CsvThousandsSeparatorConverter))]
    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    [System.ComponentModel.TypeConverter(typeof(CsvThousandsSeparatorTypeConverter))]
    public enum CsvThousandsSeparator
    {
        Comma,
        Dot,
        Space
    }

    /// <summary>Decimal separator for CSV numeric parsing. Values: Comma (,), Dot (.).</summary>
    [JsonConverter(typeof(CsvDecimalSeparatorConverter))]
    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    [System.ComponentModel.TypeConverter(typeof(CsvDecimalSeparatorTypeConverter))]
    public enum CsvDecimalSeparator
    {
        Comma,
        Dot
    }

    /// <summary>
    /// Makes the .NET Configuration binder (appsettings.json) accept both enum names
    /// ("Dot") and raw symbols (".", ",", " ") when binding CsvSeparator values.
    /// </summary>
    public abstract class SeparatorEnumTypeConverterBase<T> : System.ComponentModel.TypeConverter where T : struct, Enum
    {
        protected abstract string SymbolFor(T value);
        protected abstract bool TryParseSymbol(string symbol, out T value);

        public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext? context, Type sourceType)
            => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override object? ConvertFrom(System.ComponentModel.ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object value)
        {
            if (value is string s)
            {
                if (Enum.TryParse<T>(s, ignoreCase: true, out var byName))
                {
                    return byName;
                }
                if (TryParseSymbol(s, out var bySymbol))
                {
                    return bySymbol;
                }
                throw new System.Text.Json.JsonException(
                    $"Unknown separator '{s}'. Valid values: {string.Join(", ", Enum.GetNames<T>())} or the raw symbol.");
            }
            return base.ConvertFrom(context, culture, value);
        }

        public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext? context, Type? destinationType)
            => destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

        public override object? ConvertTo(System.ComponentModel.ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object? value, Type destinationType)
        {
            if (destinationType == typeof(string) && value is T typed)
            {
                return SymbolFor(typed);
            }
            return base.ConvertTo(context, culture, value, destinationType);
        }
    }

    public sealed class CsvThousandsSeparatorTypeConverter : SeparatorEnumTypeConverterBase<CsvThousandsSeparator>
    {
        protected override string SymbolFor(CsvThousandsSeparator value) => value switch
        {
            CsvThousandsSeparator.Comma => ",",
            CsvThousandsSeparator.Dot => ".",
            CsvThousandsSeparator.Space => " ",
            _ => ","
        };

        protected override bool TryParseSymbol(string symbol, out CsvThousandsSeparator value)
        {
            switch (symbol)
            {
                case ",": value = CsvThousandsSeparator.Comma; return true;
                case ".": value = CsvThousandsSeparator.Dot; return true;
                case " ": value = CsvThousandsSeparator.Space; return true;
                default: value = default; return false;
            }
        }
    }

    public sealed class CsvDecimalSeparatorTypeConverter : SeparatorEnumTypeConverterBase<CsvDecimalSeparator>
    {
        protected override string SymbolFor(CsvDecimalSeparator value) => value switch
        {
            CsvDecimalSeparator.Comma => ",",
            CsvDecimalSeparator.Dot => ".",
            _ => "."
        };

        protected override bool TryParseSymbol(string symbol, out CsvDecimalSeparator value)
        {
            switch (symbol)
            {
                case ",": value = CsvDecimalSeparator.Comma; return true;
                case ".": value = CsvDecimalSeparator.Dot; return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>
    /// Accepts both enum names ("Comma") and the raw symbols ("," "." " ") in JSON so
    /// appsettings.json and API requests can use either form.
    /// </summary>
    public abstract class SeparatorEnumConverterBase<T> : JsonConverter<T> where T : struct, Enum
    {
        protected abstract string SymbolFor(T value);
        protected abstract bool TryParseSymbol(string symbol, out T value);
        protected abstract T Default { get; }

        public override T Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
        {
            if (reader.TokenType == System.Text.Json.JsonTokenType.String)
            {
                var s = reader.GetString()!;
                if (Enum.TryParse<T>(s, ignoreCase: true, out var byName))
                {
                    return byName;
                }
                if (TryParseSymbol(s, out var bySymbol))
                {
                    return bySymbol;
                }
                throw new System.Text.Json.JsonException($"Unknown separator '{s}'. Valid values: {string.Join(", ", Enum.GetNames<T>())} or the raw symbol.");
            }
            if (reader.TokenType == System.Text.Json.JsonTokenType.Number)
            {
                return (T)Enum.ToObject(typeof(T), reader.GetInt32());
            }
            return Default;
        }

        public override void Write(System.Text.Json.Utf8JsonWriter writer, T value, System.Text.Json.JsonSerializerOptions options)
        {
            writer.WriteStringValue(Enum.GetName(typeof(T), value));
        }
    }

    public sealed class CsvThousandsSeparatorConverter : SeparatorEnumConverterBase<CsvThousandsSeparator>
    {
        protected override CsvThousandsSeparator Default => CsvThousandsSeparator.Comma;
        protected override string SymbolFor(CsvThousandsSeparator value) => value switch
        {
            CsvThousandsSeparator.Comma => ",",
            CsvThousandsSeparator.Dot => ".",
            _ => " "
        };
        protected override bool TryParseSymbol(string symbol, out CsvThousandsSeparator value)
        {
            switch (symbol)
            {
                case ",": value = CsvThousandsSeparator.Comma; return true;
                case ".": value = CsvThousandsSeparator.Dot; return true;
                case " ": case "space": value = CsvThousandsSeparator.Space; return true;
                default: value = Default; return false;
            }
        }
    }

    public sealed class CsvDecimalSeparatorConverter : SeparatorEnumConverterBase<CsvDecimalSeparator>
    {
        protected override CsvDecimalSeparator Default => CsvDecimalSeparator.Dot;
        protected override string SymbolFor(CsvDecimalSeparator value) => value switch
        {
            CsvDecimalSeparator.Comma => ",",
            _ => "."
        };
        protected override bool TryParseSymbol(string symbol, out CsvDecimalSeparator value)
        {
            switch (symbol)
            {
                case ".": value = CsvDecimalSeparator.Dot; return true;
                case ",": value = CsvDecimalSeparator.Comma; return true;
                default: value = Default; return false;
            }
        }
    }

    public static class CsvSeparatorExtensions
    {
        public static char ToChar(this CsvThousandsSeparator separator) => separator switch
        {
            CsvThousandsSeparator.Comma => ',',
            CsvThousandsSeparator.Dot => '.',
            _ => ' '
        };

        public static char ToChar(this CsvDecimalSeparator separator) => separator == CsvDecimalSeparator.Comma ? ',' : '.';
    }
}