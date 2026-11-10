// Copyright 2025 Crystal Ferrai
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using Newtonsoft.Json;

namespace IcarusSaveLib
{
	/// <summary>
	/// Custom Json writer that allows control over the insertion of new lines
	/// </summary>
	internal class CustomJsonWriter : JsonTextWriter
	{
		/// <summary>
		/// If true, new lines will be added when indentation is enabled.
		/// If false, indentation will be replaced with a single space.
		/// </summary>
		public bool AddNewLines { get; set; } = true;

		public CustomJsonWriter(TextWriter textWriter)
			: base(textWriter)
		{
		}

		protected override void WriteIndent()
		{
			if (AddNewLines)
			{
				base.WriteIndent();
			}
			else
			{
				WriteWhitespace(" ");
			}
		}
	}

	/// <summary>
	/// A Json converter which writes byte arrays as arrays of numbers instead of base64 strings
	/// </summary>
	internal class ByteArrayConverter : JsonConverter<byte[]>
	{
		public override void WriteJson(JsonWriter writer, byte[]? value, JsonSerializer serializer)
		{
			if (value is not null)
			{
				CustomJsonWriter? customWriter = writer as CustomJsonWriter;
				bool addNewLines = customWriter?.AddNewLines ?? true;
				if (customWriter is not null)
				{
					customWriter.AddNewLines = false;
				}

				writer.WriteStartArray();
				foreach (byte b in value)
				{
					writer.WriteValue(b);
				}
				writer.WriteEndArray();

				if (customWriter is not null)
				{
					customWriter.AddNewLines = addNewLines;
				}
			}
			else
			{
				writer.WriteNull();
			}
		}

		public override byte[]? ReadJson(JsonReader reader, Type objectType, byte[]? existingValue, bool hasExistingValue, JsonSerializer serializer)
		{
			if (reader.TokenType == JsonToken.String)
			{
				string? base64String = reader.Value as string;
				if (base64String is not null)
				{
					return Convert.FromBase64String(base64String);
				}
			}
			else if (reader.TokenType == JsonToken.StartArray)
			{
				List<byte> bytes = new();
				while (reader.Read() && reader.TokenType != JsonToken.EndArray)
				{
					if (reader.TokenType == JsonToken.Integer)
					{
						bytes.Add(Convert.ToByte(reader.Value));
					}
				}
				return bytes.ToArray();
			}
			return null;
		}
	}
}
