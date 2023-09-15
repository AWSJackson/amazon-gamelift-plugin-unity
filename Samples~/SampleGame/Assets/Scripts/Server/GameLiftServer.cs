// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: MIT-0

#if UNITY_SERVER
using System;
using System.Collections.Generic;
using Amazon;
using Amazon.GameLift;
using Amazon.GameLift.Model;
using AmazonGameLift.Editor;
using Aws.GameLift;
using Aws.GameLift.Server;
using UnityEngine;

public class GameLiftServer
{
    private readonly GameLift _gl;
    private readonly Logger _logger;
    private bool _gameLiftRequestedTermination = false;
    private int _port;
    private bool _isConnected;
    private string _logFilePath;
    private readonly CoreApi _coreApi;

    public GameLiftServer(GameLift gl, Logger logger)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _coreApi = new CoreApi(Paths.PluginConfigurationFile);
    }

    // The port must be in the range of open ports for the fleet
    public void Start(int port, string authToken = null, string logFilePath = null)
    {
        _logFilePath = logFilePath;
        _port = port;

        string sdkVersion = GameLiftServerAPI.GetSdkVersion().Result;
        _logger.Write(":) SDK VERSION: " + sdkVersion);

        try
        {
            var websocketUrl = _coreApi.GetSetting(SettingsKeys.WebsocketUrl).Value;
            var fleetID = _coreApi.GetSetting(SettingsKeys.FleetId).Value;
            var computeName = _coreApi.GetSetting(SettingsKeys.ComputeName).Value;

#if UNITY_EDITOR
            var credentialsResponse =
                _coreApi.RetrieveAwsCredentials(_coreApi.GetSetting(SettingsKeys.CurrentProfileName).Value);
            var region = _coreApi.GetSetting(SettingsKeys.CurrentRegion).Value;
            var gameLiftClient = new AmazonGameLiftClient(credentialsResponse.AccessKey, credentialsResponse.SecretKey, RegionEndpoint.GetBySystemName(region));
            authToken ??= gameLiftClient.GetComputeAuthTokenAsync(new GetComputeAuthTokenRequest { ComputeName = computeName, FleetId = fleetID}).Result.AuthToken;
#endif
            var serverParams =
                new ServerParameters(websocketUrl, Application.productName, computeName, fleetID, authToken);

            GenericOutcome initOutcome = GameLiftServerAPI.InitSDK(serverParams);

            if (initOutcome.Success)
            {
                _logger.Write(":) SERVER IS IN A GAMELIFT FLEET");
                ProcessReady();
            }
            else
            {
                SetConnected(false);

                _logger.Write(":( SERVER NOT IN A FLEET. GameLiftServerAPI.InitSDK() returned " + Environment.NewLine +
                              initOutcome.Error.ErrorMessage);
            }
        }
        catch (Exception e)
        {
            _logger.Write(":( SERVER NOT IN A FLEET. GameLiftServerAPI.InitSDK() exception " + Environment.NewLine +
                          e.Message);
        }
    }

    public void TerminateGameSession(bool processEnding)
    {
        if (_gameLiftRequestedTermination)
        {
            // don't terminate game session if gamelift initiated process termination, just exit.
            Environment.Exit(0);
        }

        try
        {
            if (processEnding)
            {
                ProcessEnding();
            }
            else
            {
                ProcessReady();
            }
        }
        catch (Exception e)
        {
            _logger.Write(":( GAME SESSION TERMINATION FAILED. TerminateGameSession() exception " +
                          Environment.NewLine + e.Message);
        }
    }

    public bool AcceptPlayerSession(string playerSessionId)
    {
        try
        {
            GenericOutcome outcome = GameLiftServerAPI.AcceptPlayerSession(playerSessionId);

            if (outcome.Success)
            {
                _logger.Write(":) Accepted Player Session: " + playerSessionId);
                return true;
            }
            else
            {
                _logger.Write(":( ACCEPT PLAYER SESSION FAILED. AcceptPlayerSession() returned " +
                              outcome.Error.ToString());
                return false;
            }
        }
        catch (Exception e)
        {
            _logger.Write(":( ACCEPT PLAYER SESSION FAILED. AcceptPlayerSession() exception " + Environment.NewLine +
                          e.Message);
            return false;
        }
    }

    public bool RemovePlayerSession(string playerSessionId)
    {
        try
        {
            GenericOutcome outcome = GameLiftServerAPI.RemovePlayerSession(playerSessionId);

            if (outcome.Success)
            {
                _logger.Write(":) Removed Player Session: " + playerSessionId);
                return true;
            }
            else
            {
                _logger.Write(":( REMOVE PLAYER SESSION FAILED. RemovePlayerSession() returned " +
                              outcome.Error.ToString());
                return false;
            }
        }
        catch (Exception e)
        {
            _logger.Write(":( REMOVE PLAYER SESSION FAILED. RemovePlayerSession() exception " + Environment.NewLine +
                          e.Message);
            return false;
        }
    }

    private void ProcessReady()
    {
        try
        {
            ProcessParameters processParams = CreateProcessParameters();
            GenericOutcome processReadyOutcome = GameLiftServerAPI.ProcessReady(processParams);
            SetConnected(processReadyOutcome.Success);

            if (processReadyOutcome.Success)
            {
                _logger.Write(":) PROCESSREADY SUCCESS.");
            }
            else
            {
                _logger.Write(":( PROCESSREADY FAILED. ProcessReady() returned " +
                              processReadyOutcome.Error.ToString());
            }
        }
        catch (Exception e)
        {
            _logger.Write(":( PROCESSREADY FAILED. ProcessReady() exception " + Environment.NewLine + e.Message);
        }
    }

    private ProcessParameters CreateProcessParameters()
    {
        var logParameters = new LogParameters();

        if (_logFilePath != null)
        {
            logParameters.LogPaths = new List<string> { _logFilePath };
        }

        return new ProcessParameters(
            onStartGameSession: gameSession =>
            {
                _logger.Write(":) GAMELIFT SESSION REQUESTED"); //And then do stuff with it maybe.
                try
                {
                    GenericOutcome outcome = GameLiftServerAPI.ActivateGameSession();

                    if (outcome.Success)
                    {
                        _logger.Write(":) GAME SESSION ACTIVATED");
                    }
                    else
                    {
                        _logger.Write(":( GAME SESSION ACTIVATION FAILED. ActivateGameSession() returned " +
                                      outcome.Error.ToString());
                    }
                }
                catch (Exception e)
                {
                    _logger.Write(":( GAME SESSION ACTIVATION FAILED. ActivateGameSession() exception " +
                                  Environment.NewLine + e.Message);
                }
            },
            onUpdateGameSession: session =>
            {
                _logger.Write(":) GAME SESSION UPDATED");
                _logger.Write(":) UPDATE REASON - " + session.UpdateReason);
                _logger.Write(":) GameSession: " + JsonUtility.ToJson(session.GameSession));
            },
            onProcessTerminate: () =>
            {
                _logger.Write(":| GAMELIFT PROCESS TERMINATION REQUESTED (OK BYE)");
                _gameLiftRequestedTermination = true;
                _gl.TerminateServer();
            },
            onHealthCheck: () =>
            {
                _logger.Write(":) GAMELIFT HEALTH CHECK REQUESTED (HEALTHY)");
                return true;
            },
            // tell the GameLift service which port to connect to this process on.
            _port,
            // unless we manage this there can only be one process per server.
            logParameters);
    }

    private void ProcessEnding()
    {
        try
        {
            GenericOutcome outcome = GameLiftServerAPI.ProcessEnding();

            if (outcome.Success)
            {
                _logger.Write(":) PROCESSENDING");
            }
            else
            {
                _logger.Write(":( PROCESSENDING FAILED. ProcessEnding() returned " + outcome.Error.ToString());
            }
        }
        catch (Exception e)
        {
            _logger.Write(":( PROCESSENDING FAILED. ProcessEnding() exception " + Environment.NewLine + e.Message);
        }
    }

    private void SetConnected(bool value)
    {
        if (_isConnected == value)
        {
            return;
        }

        _isConnected = value;
        _gl.ConnectionChangedEvent?.Invoke(value);
    }
}

#endif
