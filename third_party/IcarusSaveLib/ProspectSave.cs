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
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using UeSaveGame;
using UeSaveGame.Util;

namespace IcarusSaveLib
{
	/// <summary>
	/// Represents an Icarus prospect save file
	/// </summary>
	public class ProspectSave
	{
		[JsonProperty(PropertyName = nameof(ProspectInfo))]
		private FProspectInfo mProspectInfo;

		[JsonProperty(PropertyName = nameof(ProspectBlob))]
		private FProspectBlob mProspectBlob;

		private readonly List<FPropertyTag> mProspectData;

		[JsonIgnore]
		public FProspectInfo ProspectInfo
		{
			get => mProspectInfo;
			set => mProspectInfo = value;
		}

		[JsonIgnore]
		public FProspectBlob ProspectBlob => mProspectBlob;

		[JsonIgnore]
		public IList<FPropertyTag> ProspectData => mProspectData;

		public ProspectSave()
		{
			mProspectInfo = new FProspectInfo();
			mProspectBlob = new FProspectBlob();
			mProspectData = new List<FPropertyTag>();
		}

		/// <summary>
		/// Loads a prospect from the given stream
		/// </summary>
		/// <param name="stream">The stream to read from</param>
		public static ProspectSave? Load(Stream stream)
		{
			ProspectSave? instance;

			JsonSerializer serializer = new();
			using (StreamReader sr = new(stream, leaveOpen: true))
			{
				instance = serializer.Deserialize(sr, typeof(ProspectSave)) as ProspectSave;
			}

			if (instance is not null)
			{
				if (Equals(instance.mProspectInfo, default(FProspectInfo)) || Equals(instance.mProspectBlob, default(FProspectBlob)))
				{
					return null;
				}

				byte[] compressed = Convert.FromBase64String(instance.mProspectBlob.BinaryBlob);

				using (MemoryStream mem = new(compressed))
				using (ZLibStream zstream = new(mem, CompressionMode.Decompress))
				using (BinaryReader reader = new(zstream))
				{
					instance.mProspectData.AddRange(PropertySerializationHelper.ReadProperties(reader, ProspectSerlializationUtil.IcarusPackageVersion, false));
				}
			}

			return instance;
		}

		/// <summary>
		/// Attempts to load a prospect from the given stream
		/// </summary>
		/// <param name="stream">The stream to read from</param>
		/// <param name="save">If successful, the loaded prospect</param>
		/// <returns>True if the prospect was successfully loaded, else false</returns>
		public static bool TryLoad(Stream stream, [NotNullWhen(true)] out ProspectSave? save)
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
		/// Saves this prospect to the given stream
		/// </summary>
		/// <param name="stream">The stream to write to</param>
		public void Save(Stream stream)
		{
			using (MemoryStream mem = new())
			{
				using (BinaryWriter writer = new(mem, Encoding.ASCII, true))
				{
					PropertySerializationHelper.WriteProperties(mProspectData, writer, ProspectSerlializationUtil.IcarusPackageVersion, false);
				}
				mProspectBlob.UncompressedLength = (int)mem.Length;

				mem.Seek(0, SeekOrigin.Begin);
				using (SHA1 sha1 = SHA1.Create())
				{
					mProspectBlob.Hash = Convert.ToHexString(sha1.ComputeHash(mem)).ToLowerInvariant();
				}

				mem.Seek(0, SeekOrigin.Begin);
				using (MemoryStream memCompressed = new())
				{
					using (ZLibStream zstream = new(memCompressed, CompressionMode.Compress, true))
					{
						zstream.Write(mem.ToArray(), 0, (int)mem.Length);
					}
					mProspectBlob.DataLength = (int)memCompressed.Length;
					mProspectBlob.TotalLength = (int)memCompressed.Length;
					mProspectBlob.BinaryBlob = Convert.ToBase64String(memCompressed.ToArray());
				}
			}

			JsonSerializer serializer = new();
			serializer.Formatting = Formatting.Indented;
			serializer.NullValueHandling = NullValueHandling.Ignore;

			using (StreamWriter writer = new(stream, leaveOpen: true))
			{
				serializer.Serialize(writer, this);
			}
		}
	}

	public struct FProspectInfo
	{
		public string ProspectID;
		public string ClaimedAccountID;
		public int ClaimedAccountCharacter;
		public string ProspectDTKey;
		public string FactionMissionDTKey;
		public string LobbyName;
		public long ExpireTime;
		public string ProspectState;
		public List<FAssociatedMember> AssociatedMembers;
		public int Cost;
		public int Reward;
		public string Difficulty;
		public bool Insurance;
		public bool NoRespawns;
		public int ElapsedTime;
		public int SelectedDropPoint;
		public List<FCustomGameSetting> CustomSettings;
	}

	public struct FCustomGameSetting
	{
		public string SettingRowName;
		public int SettingValue;
	};

	public struct FProspectBlob
	{
		public string Key;
		public string Hash;
		public int TotalLength;
		public int DataLength;
		public int UncompressedLength;
		public string BinaryBlob;
	}

	public struct FAssociatedMember
	{
		public string AccountName;
		public string CharacterName;
		public string UserID;
		public int ChrSlot;
		public int Experience;
		public string Status;
		public bool Settled;
		public bool IsCurrentlyPlaying;
	}
}
