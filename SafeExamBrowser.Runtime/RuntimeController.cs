﻿/*
 * Copyright (c) 2018 ETH Zürich, Educational Development and Technology (LET)
 * 
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using System;
using System.Threading;
using SafeExamBrowser.Contracts.Communication.Data;
using SafeExamBrowser.Contracts.Communication.Events;
using SafeExamBrowser.Contracts.Communication.Hosts;
using SafeExamBrowser.Contracts.Communication.Proxies;
using SafeExamBrowser.Contracts.Configuration;
using SafeExamBrowser.Contracts.Configuration.Settings;
using SafeExamBrowser.Contracts.Core;
using SafeExamBrowser.Contracts.Core.OperationModel;
using SafeExamBrowser.Contracts.Core.OperationModel.Events;
using SafeExamBrowser.Contracts.I18n;
using SafeExamBrowser.Contracts.Logging;
using SafeExamBrowser.Contracts.UserInterface;
using SafeExamBrowser.Contracts.UserInterface.MessageBox;
using SafeExamBrowser.Contracts.UserInterface.Windows;
using SafeExamBrowser.Runtime.Operations.Events;

namespace SafeExamBrowser.Runtime
{
	internal class RuntimeController : IRuntimeController
	{
		private bool sessionRunning;

		private AppConfig appConfig;
		private IConfigurationRepository configuration;
		private ILogger logger;
		private IMessageBox messageBox;
		private IOperationSequence bootstrapSequence;
		private IRepeatableOperationSequence sessionSequence;
		private IRuntimeHost runtimeHost;
		private IRuntimeWindow runtimeWindow;
		private IServiceProxy service;
		private ISplashScreen splashScreen;
		private Action shutdown;
		private IText text;
		private IUserInterfaceFactory uiFactory;
		
		public RuntimeController(
			AppConfig appConfig,
			IConfigurationRepository configuration,
			ILogger logger,
			IMessageBox messageBox,
			IOperationSequence bootstrapSequence,
			IRepeatableOperationSequence sessionSequence,
			IRuntimeHost runtimeHost,
			IServiceProxy service,
			Action shutdown,
			IText text,
			IUserInterfaceFactory uiFactory)
		{
			this.appConfig = appConfig;
			this.configuration = configuration;
			this.bootstrapSequence = bootstrapSequence;
			this.logger = logger;
			this.messageBox = messageBox;
			this.runtimeHost = runtimeHost;
			this.sessionSequence = sessionSequence;
			this.service = service;
			this.shutdown = shutdown;
			this.text = text;
			this.uiFactory = uiFactory;
		}

		public bool TryStart()
		{
			logger.Info("Initiating startup procedure...");

			runtimeWindow = uiFactory.CreateRuntimeWindow(appConfig);
			splashScreen = uiFactory.CreateSplashScreen(appConfig);

			bootstrapSequence.ProgressChanged += BootstrapSequence_ProgressChanged;
			bootstrapSequence.StatusChanged += BootstrapSequence_StatusChanged;
			sessionSequence.ActionRequired += SessionSequence_ActionRequired;
			sessionSequence.ProgressChanged += SessionSequence_ProgressChanged;
			sessionSequence.StatusChanged += SessionSequence_StatusChanged;

			splashScreen.Show();

			var initialized = bootstrapSequence.TryPerform() == OperationResult.Success;

			if (initialized)
			{
				RegisterEvents();

				logger.Info("Application successfully initialized.");
				logger.Log(string.Empty);
				logger.Subscribe(runtimeWindow);
				splashScreen.Close();

				StartSession(true);
			}
			else
			{
				logger.Info("Application startup aborted!");
				logger.Log(string.Empty);

				messageBox.Show(TextKey.MessageBox_StartupError, TextKey.MessageBox_StartupErrorTitle, icon: MessageBoxIcon.Error, parent: splashScreen);
			}

			return initialized && sessionRunning;
		}

		public void Terminate()
		{
			DeregisterEvents();

			if (sessionRunning)
			{
				StopSession();
			}

			logger.Unsubscribe(runtimeWindow);
			runtimeWindow?.Close();

			splashScreen = uiFactory.CreateSplashScreen(appConfig);
			splashScreen.Show();

			logger.Log(string.Empty);
			logger.Info("Initiating shutdown procedure...");

			var success = bootstrapSequence.TryRevert() == OperationResult.Success;

			if (success)
			{
				logger.Info("Application successfully finalized.");
				logger.Log(string.Empty);
			}
			else
			{
				logger.Info("Shutdown procedure failed!");
				logger.Log(string.Empty);

				messageBox.Show(TextKey.MessageBox_ShutdownError, TextKey.MessageBox_ShutdownErrorTitle, icon: MessageBoxIcon.Error, parent: splashScreen);
			}

			splashScreen.Close();
		}

		private void StartSession(bool initial = false)
		{
			runtimeWindow.Show();
			runtimeWindow.BringToForeground();
			runtimeWindow.ShowProgressBar();
			logger.Info("### --- Session Start Procedure --- ###");

			if (sessionRunning)
			{
				DeregisterSessionEvents();
			}

			var result = initial ? sessionSequence.TryPerform() : sessionSequence.TryRepeat();

			if (result == OperationResult.Success)
			{
				RegisterSessionEvents();

				logger.Info("### --- Session Running --- ###");
				runtimeWindow.HideProgressBar();
				runtimeWindow.UpdateStatus(TextKey.RuntimeWindow_ApplicationRunning);
				runtimeWindow.TopMost = configuration.CurrentSettings.KioskMode != KioskMode.None;

				if (configuration.CurrentSettings.KioskMode == KioskMode.DisableExplorerShell)
				{
					runtimeWindow.Hide();
				}

				sessionRunning = true;
			}
			else
			{
				logger.Info($"### --- Session Start {(result == OperationResult.Aborted ? "Aborted" : "Failed")} --- ###");

				if (result == OperationResult.Failed)
				{
					// TODO: Find solution for this; maybe manually switch back to original desktop?
					// messageBox.Show(TextKey.MessageBox_SessionStartError, TextKey.MessageBox_SessionStartErrorTitle, icon: MessageBoxIcon.Error, parent: runtimeWindow);

					if (!initial)
					{
						logger.Info("Terminating application...");
						shutdown.Invoke();
					}
				}
			}
		}

		private void StopSession()
		{
			runtimeWindow.Show();
			runtimeWindow.BringToForeground();
			runtimeWindow.ShowProgressBar();
			logger.Info("### --- Session Stop Procedure --- ###");

			DeregisterSessionEvents();

			var success = sessionSequence.TryRevert() == OperationResult.Success;

			if (success)
			{
				logger.Info("### --- Session Terminated --- ###");
				sessionRunning = false;
			}
			else
			{
				logger.Info("### --- Session Stop Failed --- ###");
				messageBox.Show(TextKey.MessageBox_SessionStopError, TextKey.MessageBox_SessionStopErrorTitle, icon: MessageBoxIcon.Error, parent: runtimeWindow);
			}
		}

		private void RegisterEvents()
		{
			runtimeHost.ReconfigurationRequested += RuntimeHost_ReconfigurationRequested;
			runtimeHost.ShutdownRequested += RuntimeHost_ShutdownRequested;
		}

		private void DeregisterEvents()
		{
			runtimeHost.ReconfigurationRequested -= RuntimeHost_ReconfigurationRequested;
			runtimeHost.ShutdownRequested -= RuntimeHost_ShutdownRequested;
		}

		private void RegisterSessionEvents()
		{
			configuration.CurrentSession.ClientProcess.Terminated += ClientProcess_Terminated;
			configuration.CurrentSession.ClientProxy.ConnectionLost += Client_ConnectionLost;
		}

		private void DeregisterSessionEvents()
		{
			if (configuration.CurrentSession.ClientProcess != null)
			{
				configuration.CurrentSession.ClientProcess.Terminated -= ClientProcess_Terminated;
			}

			if (configuration.CurrentSession.ClientProxy != null)
			{
				configuration.CurrentSession.ClientProxy.ConnectionLost -= Client_ConnectionLost;
			}
		}

		private void BootstrapSequence_ProgressChanged(ProgressChangedEventArgs args)
		{
			MapProgress(splashScreen, args);
		}

		private void BootstrapSequence_StatusChanged(TextKey status)
		{
			splashScreen?.UpdateStatus(status, true);
		}

		private void ClientProcess_Terminated(int exitCode)
		{
			logger.Error($"Client application has unexpectedly terminated with exit code {exitCode}!");

			if (sessionRunning)
			{
				StopSession();
			}

			messageBox.Show(TextKey.MessageBox_ApplicationError, TextKey.MessageBox_ApplicationErrorTitle, icon: MessageBoxIcon.Error);

			shutdown.Invoke();
		}

		private void Client_ConnectionLost()
		{
			logger.Error("Lost connection to the client application!");

			if (sessionRunning)
			{
				StopSession();
			}

			messageBox.Show(TextKey.MessageBox_ApplicationError, TextKey.MessageBox_ApplicationErrorTitle, icon: MessageBoxIcon.Error);

			shutdown.Invoke();
		}

		private void RuntimeHost_ReconfigurationRequested(ReconfigurationEventArgs args)
		{
			var mode = configuration.CurrentSettings.ConfigurationMode;

			if (mode == ConfigurationMode.ConfigureClient)
			{
				logger.Info($"Accepted request for reconfiguration with '{args.ConfigurationPath}'.");
				configuration.ReconfigurationFilePath = args.ConfigurationPath;

				StartSession();
			}
			else
			{
				logger.Info($"Denied request for reconfiguration with '{args.ConfigurationPath}' due to '{mode}' mode!");
				configuration.CurrentSession.ClientProxy.InformReconfigurationDenied(args.ConfigurationPath);
			}
		}

		private void RuntimeHost_ShutdownRequested()
		{
			logger.Info("Received shutdown request from the client application.");
			shutdown.Invoke();
		}

		private void SessionSequence_ActionRequired(ActionRequiredEventArgs args)
		{
			switch (args)
			{
				case ConfigurationCompletedEventArgs a:
					AskIfConfigurationSufficient(a);
					break;
				case PasswordRequiredEventArgs p:
					AskForPassword(p);
					break;
			}
		}

		private void AskIfConfigurationSufficient(ConfigurationCompletedEventArgs args)
		{
			var message = TextKey.MessageBox_ClientConfigurationQuestion;
			var title = TextKey.MessageBox_ClientConfigurationQuestionTitle;
			var result = messageBox.Show(message, title, MessageBoxAction.YesNo, MessageBoxIcon.Question, runtimeWindow);

			args.AbortStartup = result == MessageBoxResult.Yes;
		}

		private void AskForPassword(PasswordRequiredEventArgs args)
		{
			var isStartup = configuration.CurrentSession == null;
			var isRunningOnDefaultDesktop = configuration.CurrentSettings?.KioskMode == KioskMode.DisableExplorerShell;

			if (isStartup || isRunningOnDefaultDesktop)
			{
				TryGetPasswordViaDialog(args);
			}
			else
			{
				TryGetPasswordViaClient(args);
			}
		}

		private void TryGetPasswordViaDialog(PasswordRequiredEventArgs args)
		{
			var isAdmin = args.Purpose == PasswordRequestPurpose.Administrator;
			var message = isAdmin ? TextKey.PasswordDialog_AdminPasswordRequired : TextKey.PasswordDialog_SettingsPasswordRequired;
			var title = isAdmin ? TextKey.PasswordDialog_AdminPasswordRequiredTitle : TextKey.PasswordDialog_SettingsPasswordRequiredTitle;
			var dialog = uiFactory.CreatePasswordDialog(text.Get(message), text.Get(title));
			var result = dialog.Show(runtimeWindow);

			args.Password = result.Password;
			args.Success = result.Success;
		}

		private void TryGetPasswordViaClient(PasswordRequiredEventArgs args)
		{
			var requestId = Guid.NewGuid();
			var response = default(PasswordReplyEventArgs);
			var responseEvent = new AutoResetEvent(false);
			var responseEventHandler = new CommunicationEventHandler<PasswordReplyEventArgs>((a) =>
			{
				if (a.RequestId == requestId)
				{
					response = a;
					responseEvent.Set();
				}
			});

			runtimeHost.PasswordReceived += responseEventHandler;

			var communication = configuration.CurrentSession.ClientProxy.RequestPassword(args.Purpose, requestId);

			if (communication.Success)
			{
				responseEvent.WaitOne();
				args.Password = response.Password;
				args.Success = response.Success;
			}
			else
			{
				args.Password = default(string);
				args.Success = false;
			}

			runtimeHost.PasswordReceived -= responseEventHandler;
		}

		private void SessionSequence_ProgressChanged(ProgressChangedEventArgs args)
		{
			MapProgress(runtimeWindow, args);
		}

		private void SessionSequence_StatusChanged(TextKey status)
		{
			runtimeWindow?.UpdateStatus(status, true);
		}

		private void MapProgress(IProgressIndicator progressIndicator, ProgressChangedEventArgs args)
		{
			if (args.CurrentValue.HasValue)
			{
				progressIndicator?.SetValue(args.CurrentValue.Value);
			}

			if (args.IsIndeterminate == true)
			{
				progressIndicator?.SetIndeterminate();
			}

			if (args.MaxValue.HasValue)
			{
				progressIndicator?.SetMaxValue(args.MaxValue.Value);
			}

			if (args.Progress == true)
			{
				progressIndicator?.Progress();
			}

			if (args.Regress == true)
			{
				progressIndicator?.Regress();
			}
		}
	}
}
