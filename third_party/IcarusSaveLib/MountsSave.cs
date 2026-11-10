// Copyright 2026 Crystal Ferrai
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
using System.Diagnostics.CodeAnalysis;
using UeSaveGame;
using UeSaveGame.Util;

namespace IcarusSaveLib
{
	/// <summary>
	/// Represents an Icarus mounts save file
	/// </summary>
	public class MountsSave
	{
		[JsonProperty(PropertyName = nameof(SavedMounts))]
		private readonly List<FMountSaveData> mSavedMountsData;

		private readonly List<SavedMount> mSavedMounts;

		[JsonIgnore]
		public IList<SavedMount> SavedMounts => mSavedMounts;

		public MountsSave()
		{
			mSavedMountsData = new();
			mSavedMounts = new();
		}

		/// <summary>
		/// Loads a mounts save from the given stream
		/// </summary>
		/// <param name="stream">The stream to read from</param>
		public static MountsSave? Load(Stream stream)
		{
			MountsSave? instance;

			JsonSerializer serializer = new();
			using (StreamReader reader = new(stream, leaveOpen: true))
			{
				instance = serializer.Deserialize(reader, typeof(MountsSave)) as MountsSave;
			}

			if (instance is null || instance.mSavedMountsData.Count == 0)
			{
				return null;
			}

			foreach (FMountSaveData data in instance.mSavedMountsData)
			{
				instance.mSavedMounts.Add(SavedMount.Load(data));
			}

			return instance;
		}

		/// <summary>
		/// Attempts to load a mounts save from the given stream
		/// </summary>
		/// <param name="stream">The stream to read from</param>
		/// <param name="save">If successful, the loaded save</param>
		/// <returns>True if the save was successfully loaded, else false</returns>
		public static bool TryLoad(Stream stream, [NotNullWhen(true)] out MountsSave? save)
		{
			long position = 0L;
			try
			{
				if (stream.CanSeek)
				{
					position = stream.Position;
				}
				save = Load(stream);
				if (save is null && stream.CanSeek)
				{
					stream.Seek(position, SeekOrigin.Begin);
				}
				return save != null;
			}
			catch
			{
				if (stream.CanSeek)
				{
					stream.Seek(position, SeekOrigin.Begin);
				}
				save = null;
				return false;
			}
		}

		/// <summary>
		/// Saves this mounts save to the given stream
		/// </summary>
		/// <param name="stream">The stream to write to</param>
		public void Save(Stream stream)
		{
			mSavedMountsData.Clear();
			foreach (SavedMount mount in mSavedMounts)
			{
				mount.Save();
				mSavedMountsData.Add(mount.SaveData);
			}

			JsonSerializer serializer = new();
			serializer.Formatting = Formatting.Indented;
			serializer.NullValueHandling = NullValueHandling.Ignore;
			serializer.Converters.Add(new ByteArrayConverter());

			using (StreamWriter writer = new(stream, leaveOpen: true))
			using (JsonWriter jsonWriter = new CustomJsonWriter(writer) { Indentation = 1, IndentChar = '\t' })
			{
				serializer.Serialize(jsonWriter, this);
			}
		}
	}

	/// <summary>
	/// A saved mount
	/// </summary>
	public class SavedMount
	{
		private FMountSaveData mSaveData;
		private SavedMountRecorderData? mRecorderData;

		/// <summary>
		/// Summary save data
		/// </summary>
		public FMountSaveData SaveData
		{
			get => mSaveData;
			set => mSaveData = value;
		}

		/// <summary>
		/// Detailed save data
		/// </summary>
		public SavedMountRecorderData? RecorderData
		{
			get => mRecorderData;
			set => mRecorderData = value;
		}

		public SavedMount()
		{
		}

		public static SavedMount Load(FMountSaveData data)
		{
			return new()
			{
				mSaveData = data,
				mRecorderData = SavedMountRecorderData.Load(data.RecorderBlob)
			};
		}

		public void Save()
		{
			if (mRecorderData is not null)
			{
				mSaveData.RecorderBlob = mRecorderData.Save();
			}
		}
	}

	/// <summary>
	/// Recorded data for a mount
	/// </summary>
	public class SavedMountRecorderData
	{
		private List<FPropertyTag> mProperties;

		/// <summary>
		/// The recorder component class name
		/// </summary>
		public string ComponentClassName { get; }

		/// <summary>
		/// The saved properties
		/// </summary>
		public IList<FPropertyTag> Properties => mProperties;

		public SavedMountRecorderData(string componentClassName)
		{
			mProperties = new();
			ComponentClassName = componentClassName;
		}

		internal static SavedMountRecorderData Load(FStateRecorderBlob blob)
		{
			SavedMountRecorderData instance = new(blob.ComponentClassName);
			using (MemoryStream memoryStream = new(blob.BinaryData))
			using (BinaryReader reader = new(memoryStream))
			{
				instance.mProperties.AddRange(PropertySerializationHelper.ReadProperties(reader, ProspectSerlializationUtil.IcarusPackageVersion, true));
			}
			return instance;
		}

		internal FStateRecorderBlob Save()
		{
			using (MemoryStream memoryStream = new())
			using (BinaryWriter writer = new(memoryStream))
			{
				PropertySerializationHelper.WriteProperties(mProperties, writer, ProspectSerlializationUtil.IcarusPackageVersion, true);
				writer.Flush();
				return new FStateRecorderBlob()
				{
					ComponentClassName = ComponentClassName,
					BinaryData = memoryStream.ToArray()
				};
			}
		}
	}

#pragma warning disable CS0649 // "field never set" - is set by Json deserializer
	public struct FMountSaveData
	{
		public string DatabaseGUID;
		[JsonProperty]
		internal FStateRecorderBlob RecorderBlob;
		public string MountName;
		public int MountLevel;
		public string MountType;
		public string MountIconName;
	}

	internal struct FStateRecorderBlob
	{
		public string ComponentClassName;
		public byte[] BinaryData;
	}
#pragma warning restore CS0649
}
