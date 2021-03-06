﻿using System;
using System.Collections.Generic;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Text;
using ComponentBind;
using Lemma.Components;
using Lemma.Console;
using Newtonsoft.Json;
using Steamworks;

namespace Lemma.Util
{
	public static class SteamWorker
	{
		public static DirectoryInfo DownloadedMaps
		{
			get
			{
				string completedDirectory = SteamWorker.WSFilesDirectory;
				if (!Directory.Exists(completedDirectory))
					Directory.CreateDirectory(completedDirectory);
				return new DirectoryInfo(completedDirectory);
			}
		}

		private static uint ugcPage = 0;

		private static Dictionary<string, bool> _achievementDictionary;
		private static Dictionary<string, int> _statDictionary;

		private static DateTime _statsLastUploaded = DateTime.Now;

		private static bool _anythingChanged = false;

		public static bool SteamInitialized { get; private set; }

		public static bool StatsInitialized { get; private set; }

		public static Property<bool> OverlayActive = new Property<bool>() { Value = false };

		public static bool OverlaySafelyGone
		{
			get { return _overlayTimer <= 0; }
		}

		private static float _overlayTimer = 0f;

		public static bool Initialized
		{
			get
			{
				return SteamInitialized && StatsInitialized;
			}
		}

		private static List<object> callbacks = new List<object>();

		private static CallResult<SteamUGCQueryCompleted_t> queryResult;

		private static bool Init_SteamGame()
		{
			//Getting here means Steamworks MUST have initialized successfully. Oh, && operator!
			RemoveWSTemp();
			callbacks.Add(Callback<SteamUGCQueryCompleted_t>.Create(OnUGCQueryReturn));
			callbacks.Add(Callback<UserStatsReceived_t>.Create(OnUserStatsReceived));
			callbacks.Add(Callback<RemoteStoragePublishedFileSubscribed_t>.Create(OnSubscribed));
			callbacks.Add(Callback<GameOverlayActivated_t>.Create(OnOverlayActivated));

			queryResult = QuerySubscribed();
			if (!SteamUserStats.RequestCurrentStats()) return false;
			_achievementDictionary = new Dictionary<string, bool>();
			_statDictionary = new Dictionary<string, int>();

			return true;
		}

		public static bool Init()
		{
			try
			{
#if STEAMWORKS
				return SteamInitialized = (SteamAPI.Init() && Init_SteamGame());
#else
				return (SteamInitialized = false) && false;
#endif
			}
			catch (DllNotFoundException)
			{
				Log.d("Steam DLL not found.");
				//Required DLLs ain't there
				SteamInitialized = false;
				return false;
			}
		}

		public static void Shutdown()
		{
#if STEAMWORKS
			SteamAPI.Shutdown();
#endif
		}

		public static void Update(float dt)
		{
#if STEAMWORKS
			if (SteamInitialized)
				SteamAPI.RunCallbacks();
			if (_overlayTimer > 0)
				_overlayTimer -= dt;
#endif
		}

		private static string WSFilesDirectory
		{
			get
			{
				return Path.Combine(Main.DataDirectory, "workshop");
			}
		}

		private static string WSTempDirectory
		{
			get
			{
				return Path.Combine(Main.DataDirectory, "tmp");
			}
		}

		public static void RemoveWSTemp()
		{
			string tempDirectory = SteamWorker.WSTempDirectory;
			if (!Directory.Exists(tempDirectory))
				Directory.CreateDirectory(tempDirectory);

			foreach (var dir in Directory.GetDirectories(tempDirectory))
				Directory.Delete(dir, true);

			foreach (var file in Directory.GetFiles(tempDirectory))
				File.Delete(file);
		}

		public static bool WriteFileUGC(string path, string steamPath)
		{
			if (!SteamInitialized) return false;
			if (!File.Exists(path)) return false;
			byte[] data = File.ReadAllBytes(path);
			return SteamRemoteStorage.FileWrite(steamPath, data, data.Length);
		}

		public static CallResult<RemoteStorageFileShareResult_t> ShareFileUGC(string path, Action<bool, UGCHandle_t> onShare = null)
		{
			var callResult = new CallResult<RemoteStorageFileShareResult_t>((result, failure) =>
			{
				if (result.m_eResult == EResult.k_EResultOK)
				{
					if (onShare != null)
						onShare(true, result.m_hFile);
				}
				else
				{
					if (onShare != null)
						onShare(false, result.m_hFile);
				}
			});
			callResult.Set(SteamRemoteStorage.FileShare(path));
			return callResult;
		}

		public static CallResult<RemoteStorageUpdatePublishedFileResult_t> UpdateWorkshopMap(PublishedFileId_t workshop, string pchFile, string imageFile, string name, string description, Action<bool> onDone)
		{
			var updateHandle = SteamRemoteStorage.CreatePublishedFileUpdateRequest(workshop);
			SteamRemoteStorage.UpdatePublishedFileTitle(updateHandle, name);
			SteamRemoteStorage.UpdatePublishedFileDescription(updateHandle, description);
			SteamRemoteStorage.UpdatePublishedFileFile(updateHandle, pchFile);
			SteamRemoteStorage.UpdatePublishedFilePreviewFile(updateHandle, imageFile);
			var call = SteamRemoteStorage.CommitPublishedFileUpdate(updateHandle);
			var callResult = new CallResult<RemoteStorageUpdatePublishedFileResult_t>((result, failure) =>
			{
				if (onDone != null)
					onDone(result.m_eResult == EResult.k_EResultOK && !failure);
			});
			callResult.Set(call);
			return callResult;
		}

		public static CallResult<RemoteStoragePublishFileResult_t> UploadWorkShop(string mapFile, string imageFile, string title, string description, Action<bool, bool, PublishedFileId_t> onDone)
		{
			var call = SteamRemoteStorage.PublishWorkshopFile(mapFile, imageFile, new AppId_t(Main.SteamAppID), title, description,
				ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic, new List<string>(),
				EWorkshopFileType.k_EWorkshopFileTypeCommunity);

			var callResult = new CallResult<RemoteStoragePublishFileResult_t>((result, failure) =>
			{
				onDone(result.m_eResult == EResult.k_EResultOK, result.m_bUserNeedsToAcceptWorkshopLegalAgreement, result.m_nPublishedFileId);
			});
			callResult.Set(call);
			return callResult;
		}

		[AutoConCommand("set_stat", "Set a Steam stat")]
		public static void SetStat(string name, int newVal)
		{
			if (!Initialized) return;
			if (!_statDictionary.ContainsKey(name)) return;

			var curVal = _statDictionary[name];
			if (curVal == newVal) return;
			_statDictionary[name] = newVal;
			SteamUserStats.SetStat(name, newVal);
			_anythingChanged = true;

			if ((DateTime.Now - _statsLastUploaded).TotalSeconds >= 5)
				UploadStats();
		}

		public static void IncrementStat(string name, int increment)
		{
			if (!Initialized) return;
			if (!_statDictionary.ContainsKey(name)) return;
			SetStat(name, GetStat(name) + increment);
		}

		public static bool IsStat(string name)
		{
			if (!Initialized) return false;
			return _statDictionary.ContainsKey(name);
		}

		public static int GetStat(string name)
		{
			if (!Initialized) return 0;
			if (!_statDictionary.ContainsKey(name)) return 0;
			return _statDictionary[name];
		}

		public static void SetAchievement(string name, bool forceUpload = true)
		{
			if (!Initialized) return;
			if (!_achievementDictionary.ContainsKey(name)) return;
			if (_achievementDictionary[name]) return; //No use setting an already-unlocked cheevo.
			_achievementDictionary[name] = true;
			SteamUserStats.SetAchievement(name);
			_anythingChanged = true;
			if (forceUpload)
				UploadStats();
		}

		[AutoConCommand("upload_stats", "Upload stats to Steam")]
		public static void UploadStats(bool force = false)
		{
			if (!Initialized) return;
			if (!_anythingChanged && !force) return;
			SteamUserStats.StoreStats();
			_anythingChanged = false;
			_statsLastUploaded = DateTime.Now;
		}

		[AutoConCommand("reset_stats", "Reset all stats.")]
		public static void ResetAllStats(bool andCheevos = true)
		{
			if (!SteamInitialized) return;
			SteamUserStats.ResetAllStats(andCheevos);
			SteamUserStats.RequestCurrentStats();

			_achievementDictionary = new Dictionary<string, bool>();
			_statDictionary = new Dictionary<string, int>();
			StatsInitialized = false;
		}

		public class WorkshopMapMetadata
		{
			public string PchFileName;
			public string Title;
		}

		public static Property<int> Downloading = new Property<int>();
		public static Command OnLevelDownloaded = new Command();
		private static CallResult<RemoteStorageDownloadUGCResult_t> downloadResult;
		private static CallResult<RemoteStorageGetPublishedFileDetailsResult_t> DownloadLevel(PublishedFileId_t file)
		{
			var publishedCall = SteamRemoteStorage.GetPublishedFileDetails(file, 0);

			string tempDirectory = SteamWorker.WSTempDirectory;
			string completedDirectory = SteamWorker.WSFilesDirectory;

			if (!Directory.Exists(tempDirectory))
				Directory.CreateDirectory(tempDirectory);
			if (!Directory.Exists(completedDirectory))
				Directory.CreateDirectory(completedDirectory);

			string tempDirectoryNew = Path.Combine(tempDirectory, file.m_PublishedFileId.ToString());
			string completedDirectoryNew = Path.Combine(completedDirectory, file.m_PublishedFileId.ToString());

			if (!Directory.Exists(tempDirectoryNew))
				Directory.CreateDirectory(tempDirectoryNew);
			if (!Directory.Exists(completedDirectoryNew))
				Directory.CreateDirectory(completedDirectoryNew);

			string metadataPath = Path.Combine(completedDirectoryNew, "meta.json");
			WorkshopMapMetadata metadata = null;
			try
			{
				metadata = JsonConvert.DeserializeObject<WorkshopMapMetadata>(File.ReadAllText(metadataPath));
			}
			catch (Exception)
			{

			}

			var callResult = new CallResult<RemoteStorageGetPublishedFileDetailsResult_t>((t, failure) =>
			{
				if (!failure && t.m_eResult == EResult.k_EResultOK)
				{
					if (metadata == null || metadata.PchFileName != t.m_pchFileName)
					{
						Downloading.Value++;

						string mapFileTemp = Path.Combine(tempDirectoryNew, file.m_PublishedFileId.ToString() + IO.MapLoader.MapExtension);
						string imageFileTemp = Path.Combine(tempDirectoryNew, file.m_PublishedFileId.ToString() + ".png");
						var downloadMapCall = SteamRemoteStorage.UGCDownloadToLocation(t.m_hFile, mapFileTemp, 1);
						downloadResult = new CallResult<RemoteStorageDownloadUGCResult_t>((resultT, ioFailure) =>
						{
							if (ioFailure)
							{
								Downloading.Value = Math.Max(0, Downloading - 1);
							}
							else
							{
								Action<RemoteStorageDownloadUGCResult_t, bool> onImgDownloaded = (resultT2, ioFailure2) =>
								{
									Downloading.Value = Math.Max(0, Downloading - 1);
									if (!ioFailure2)
									{
										string mapFileNew = mapFileTemp.Replace(tempDirectoryNew, completedDirectoryNew);
										if (File.Exists(mapFileNew))
											File.Delete(mapFileNew);
										File.Move(mapFileTemp, mapFileNew);
										string imageFileNew = imageFileTemp.Replace(tempDirectoryNew, completedDirectoryNew);
										if (File.Exists(imageFileNew))
											File.Delete(imageFileNew);
										File.Move(imageFileTemp, imageFileNew);
										File.WriteAllText(metadataPath, JsonConvert.SerializeObject(new WorkshopMapMetadata { PchFileName = t.m_pchFileName, Title = t.m_rgchTitle }));
										OnLevelDownloaded.Execute();
									}
								};
								var downloadImageCall = SteamRemoteStorage.UGCDownloadToLocation(t.m_hPreviewFile, imageFileTemp, 1);
								downloadResult = new CallResult<RemoteStorageDownloadUGCResult_t>(
									(ugcResultT, bIoFailure) => onImgDownloaded(ugcResultT, bIoFailure));
								downloadResult.Set(downloadImageCall);
							}
						});
						downloadResult.Set(downloadMapCall);
					}
					else if (metadata.Title != t.m_rgchTitle)
					{
						metadata.Title = t.m_rgchTitle;
						File.WriteAllText(metadataPath, JsonConvert.SerializeObject(metadata));
					}
				}
			});
			callResult.Set(publishedCall);
			return callResult;
		}

		private static CallResult<SteamUGCQueryCompleted_t> QuerySubscribed()
		{
			ugcPage++;

			var query = SteamUGC.CreateQueryUserUGCRequest(SteamUser.GetSteamID().GetAccountID(),
			   EUserUGCList.k_EUserUGCList_Subscribed, EUGCMatchingUGCType.k_EUGCMatchingUGCType_UsableInGame,
			   EUserUGCListSortOrder.k_EUserUGCListSortOrder_SubscriptionDateDesc, new AppId_t(Main.SteamAppID), new AppId_t(Main.SteamAppID),
			   ugcPage);

			var call = SteamUGC.SendQueryUGCRequest(query);

			var callResult = new CallResult<SteamUGCQueryCompleted_t>((t, failure) =>
			{
				OnUGCQueryReturn(t);
			});
			callResult.Set(call);
			return callResult;
		}

		public static CallResult<SteamUGCQueryCompleted_t> GetCreatedWorkShopEntries(Action<IEnumerable<SteamUGCDetails_t>> onResult)
		{
			var query = SteamUGC.CreateQueryUserUGCRequest(SteamUser.GetSteamID().GetAccountID(),
			   EUserUGCList.k_EUserUGCList_Published, EUGCMatchingUGCType.k_EUGCMatchingUGCType_UsableInGame,
			   EUserUGCListSortOrder.k_EUserUGCListSortOrder_LastUpdatedDesc, new AppId_t(Main.SteamAppID), new AppId_t(Main.SteamAppID),
			   1);

			var call = SteamUGC.SendQueryUGCRequest(query);

			var callResult = new CallResult<SteamUGCQueryCompleted_t>((t, failure) =>
			{
				if (onResult == null)
					return;

				List<SteamUGCDetails_t> ret = new List<SteamUGCDetails_t>();

				for (uint i = 0; i < t.m_unNumResultsReturned; i++)
				{
					var deets = new SteamUGCDetails_t();
					if (SteamUGC.GetQueryUGCResult(t.m_handle, i, out deets))
					{
						if (deets.m_nConsumerAppID.m_AppId == Main.SteamAppID && deets.m_eFileType == EWorkshopFileType.k_EWorkshopFileTypeCommunity)
						{
							ret.Add(deets);
						}
					}
				}

				onResult(ret);
			});
			callResult.Set(call);
			return callResult;
		}

		#region Callbacks

		private static void OnOverlayActivated(GameOverlayActivated_t callback)
		{
			OverlayActive.Value = callback.m_bActive != 0;
			if (!OverlayActive) _overlayTimer = 0.2f;
		}

		private static List<string> directories = new List<string>();
		private static void OnUGCQueryReturn(SteamUGCQueryCompleted_t handle)
		{
			if (ugcPage == 1)
			{
				directories.Clear();
				foreach (var s in SteamWorker.DownloadedMaps.GetDirectories())
				{
					directories.Add(s.Name);
				}
			}

			for (uint i = 0; i < handle.m_unNumResultsReturned; i++)
			{
				SteamUGCDetails_t deets;
				if (SteamUGC.GetQueryUGCResult(handle.m_handle, i, out deets))
				{
					directories.Remove(deets.m_nPublishedFileId.m_PublishedFileId.ToString());
					if (deets.m_nConsumerAppID.m_AppId == Main.SteamAppID && deets.m_eFileType == EWorkshopFileType.k_EWorkshopFileTypeCommunity)
					{
						DownloadLevel(deets.m_nPublishedFileId);
					}
				}
			}
			if (handle.m_unTotalMatchingResults > handle.m_unNumResultsReturned && handle.m_unNumResultsReturned != 0)
				queryResult = QuerySubscribed();
			else
			{
				//This whole ordeal deletes folders in here that are not currently-subscribed workshop maps.
				foreach (var dir in directories)
				{
					Directory.Delete(Path.Combine(DownloadedMaps.FullName, dir), true);
				}
			}

		}

		private static CallResult<RemoteStorageGetPublishedFileDetailsResult_t> downloadLevelResult;
		private static void OnSubscribed(RemoteStoragePublishedFileSubscribed_t subscribed)
		{
			if (!SteamInitialized) return;

			//Because Steam can be an idiot sometimes!
			if (subscribed.m_nAppID.m_AppId != Main.SteamAppID) return;

			downloadLevelResult = DownloadLevel(subscribed.m_nPublishedFileId);
		}

		private static void OnUserStatsReceived(UserStatsReceived_t pCallback)
		{
			if (!SteamInitialized) return;

			if (pCallback.m_nGameID != Main.SteamAppID || pCallback.m_eResult != EResult.k_EResultOK) return;

			//I'm sorry, Evan. We'll need to find somewhere nice to put this part.
			string[] cheevoNames = new string[]
			{
				"ending_a",
				"ending_b",
				"ending_c",
				"ending_d",
				"ending_e",
				"cheating_jerk",
				"pillar_crushed",
				"orbs",
			};
			string[] statNames = new string[]
			{
				"orbs_collected"
			};

			foreach (var cheevo in cheevoNames)
			{
				bool value;
				bool success = SteamUserStats.GetAchievement("cheevo_" + cheevo, out value);
				if (success)
					_achievementDictionary.Add("cheevo_" + cheevo, value);
			}

			foreach (var stat in statNames)
			{
				int value;
				bool success = SteamUserStats.GetStat("stat_" + stat, out value);
				if (success)
					_statDictionary.Add("stat_" + stat, value);
			}
			StatsInitialized = true;
		}
		#endregion

	}
}