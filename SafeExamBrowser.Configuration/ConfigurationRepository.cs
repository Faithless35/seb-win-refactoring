﻿/*
 * Copyright (c) 2018 ETH Zürich, Educational Development and Technology (LET)
 * 
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using System;
using System.IO;
using SafeExamBrowser.Contracts.Configuration;
using SafeExamBrowser.Contracts.Configuration.Settings;
using SafeExamBrowser.Contracts.Logging;

namespace SafeExamBrowser.Configuration
{
	public class ConfigurationRepository : IConfigurationRepository
	{
		private const string BASE_ADDRESS = "net.pipe://localhost/safeexambrowser";

		private readonly string executablePath;
		private readonly string programCopyright;
		private readonly string programTitle;
		private readonly string programVersion;

		private AppConfig appConfig;

		public ISessionData CurrentSession { get; private set; }
		public Settings CurrentSettings { get; private set; }
		public string ReconfigurationFilePath { get; set; }

		public AppConfig AppConfig
		{
			get
			{
				if (appConfig == null)
				{
					InitializeAppConfig();
				}

				return appConfig;
			}
		}

		public ConfigurationRepository(string executablePath, string programCopyright, string programTitle, string programVersion)
		{
			this.executablePath = executablePath ?? throw new ArgumentNullException(nameof(executablePath));
			this.programCopyright = programCopyright ?? throw new ArgumentNullException(nameof(programCopyright));
			this.programTitle = programTitle ?? throw new ArgumentNullException(nameof(programTitle));
			this.programVersion = programVersion ?? throw new ArgumentNullException(nameof(programVersion));
		}

		public ClientConfiguration BuildClientConfiguration()
		{
			return new ClientConfiguration
			{
				AppConfig = AppConfig,
				SessionId = CurrentSession.Id,
				Settings = CurrentSettings
			};
		}

		public void InitializeSessionConfiguration()
		{
			CurrentSession = new SessionData
			{
				Id = Guid.NewGuid(),
				NewDesktop = CurrentSession?.NewDesktop,
				OriginalDesktop = CurrentSession?.OriginalDesktop,
				StartupToken = Guid.NewGuid()
			};

			UpdateAppConfig();
		}

		public LoadStatus LoadSettings(Uri resource, string settingsPassword = null, string adminPassword = null)
		{
			// TODO: Implement loading mechanism

			LoadDefaultSettings();

			return LoadStatus.Success;
		}

		public void LoadDefaultSettings()
		{
			// TODO: Implement default settings

			CurrentSettings = new Settings();

			CurrentSettings.KioskMode = KioskMode.None;
			CurrentSettings.ServicePolicy = ServicePolicy.Optional;

			CurrentSettings.Browser.StartUrl = "https://www.safeexambrowser.org/testing";
			CurrentSettings.Browser.AllowAddressBar = true;
			CurrentSettings.Browser.AllowBackwardNavigation = true;
			CurrentSettings.Browser.AllowDeveloperConsole = true;
			CurrentSettings.Browser.AllowForwardNavigation = true;
			CurrentSettings.Browser.AllowReloading = true;
			CurrentSettings.Browser.AllowDownloads = true;

			CurrentSettings.Taskbar.AllowApplicationLog = true;
			CurrentSettings.Taskbar.AllowKeyboardLayout = true;
			CurrentSettings.Taskbar.AllowWirelessNetwork = true;
		}

		private void InitializeAppConfig()
		{
			var appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), nameof(SafeExamBrowser));
			var startTime = DateTime.Now;
			var logFolder = Path.Combine(appDataFolder, "Logs");
			var logFilePrefix = startTime.ToString("yyyy-MM-dd\\_HH\\hmm\\mss\\s");

			appConfig = new AppConfig();
			appConfig.ApplicationStartTime = startTime;
			appConfig.AppDataFolder = appDataFolder;
			appConfig.BrowserCachePath = Path.Combine(appDataFolder, "Cache");
			appConfig.BrowserLogFile = Path.Combine(logFolder, $"{logFilePrefix}_Browser.log");
			appConfig.ClientId = Guid.NewGuid();
			appConfig.ClientAddress = $"{BASE_ADDRESS}/client/{Guid.NewGuid()}";
			appConfig.ClientExecutablePath = Path.Combine(Path.GetDirectoryName(executablePath), $"{nameof(SafeExamBrowser)}.Client.exe");
			appConfig.ClientLogFile = Path.Combine(logFolder, $"{logFilePrefix}_Client.log");
			appConfig.ConfigurationFileExtension = ".seb";
			appConfig.DefaultSettingsFileName = "SebClientSettings.seb";
			appConfig.DownloadDirectory = Path.Combine(appDataFolder, "Downloads");
			appConfig.LogLevel = LogLevel.Debug;
			appConfig.ProgramCopyright = programCopyright;
			appConfig.ProgramDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), nameof(SafeExamBrowser));
			appConfig.ProgramTitle = programTitle;
			appConfig.ProgramVersion = programVersion;
			appConfig.RuntimeId = Guid.NewGuid();
			appConfig.RuntimeAddress = $"{BASE_ADDRESS}/runtime/{Guid.NewGuid()}";
			appConfig.RuntimeLogFile = Path.Combine(logFolder, $"{logFilePrefix}_Runtime.log");
			appConfig.SebUriScheme = "seb";
			appConfig.SebUriSchemeSecure = "sebs";
			appConfig.ServiceAddress = $"{BASE_ADDRESS}/service";
		}

		private void UpdateAppConfig()
		{
			AppConfig.ClientId = Guid.NewGuid();
			AppConfig.ClientAddress = $"{BASE_ADDRESS}/client/{Guid.NewGuid()}";
		}
	}
}
