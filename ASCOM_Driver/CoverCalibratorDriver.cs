/*
 * CoverCalibratorDriver.cs
 * Copyright (C) 2022 - Present, Julien Lecomte - All Rights Reserved
 * Licensed under the MIT License. See the accompanying LICENSE file for terms.
 */

using ASCOM.DeviceInterface;
using ASCOM.Utilities;

using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;

namespace ASCOM.DarkSkyGeek
{
    //
    // Your driver's DeviceID is ASCOM.DarkSkyGeek.CoverCalibrator
    //
    // The Guid attribute sets the CLSID for ASCOM.DarkSkyGeek.CoverCalibrator
    // The ClassInterface/None attribute prevents an empty interface called
    // _DarkSkyGeek from being created and used as the [default] interface
    //

    /// <summary>
    /// ASCOM CoverCalibrator Driver for DarkSkyGeek.
    /// </summary>
    [Guid("b034dc4e-3ea6-4ed3-9c41-b7a462337972")]
    [ClassInterface(ClassInterfaceType.None)]
    public class CoverCalibrator : ICoverCalibratorV1
    {
        /// <summary>
        /// ASCOM DeviceID (COM ProgID) for this driver.
        /// The DeviceID is used by ASCOM applications to load the driver at runtime.
        /// </summary>
        internal static string driverID = "ASCOM.DarkSkyGeek.CoverCalibrator";

        /// <summary>
        /// Driver description that displays in the ASCOM Chooser.
        /// </summary>
        private static string deviceName = "DarkSkyGeek’s Flat Panel";

        // Constants used for Profile persistence
        internal static string autoDetectComPortProfileName = "Auto-Detect COM Port";
        internal static string autoDetectComPortDefault = "true";
        internal static string comPortProfileName = "COM Port";
        internal static string comPortDefault = "COM1";
        internal static string traceStateProfileName = "Trace Level";
        internal static string traceStateDefault = "false";

        // Variables to hold the current device configuration
        internal static bool autoDetectComPort = Convert.ToBoolean(autoDetectComPortDefault);
        internal static string comPortOverride = comPortDefault;

        /// <summary>
        /// Private variable to hold the connected state
        /// </summary>
        private bool connectedState;

        /// <summary>
        /// Private variable to hold the COM port we are actually connected to
        /// </summary>
        private string comPort;

        /// <summary>
        /// Variable to hold the trace logger object (creates a diagnostic log file with information that you specify)
        /// </summary>
        internal TraceLogger tl;

        // The object used to communicate with the device using serial port communication.
        private Serial objSerial;

        // Constants used to communicate with the device
        // Make sure those values are identical to those in the Arduino Firmware.
        // (I could not come up with an easy way to share them across the two projects)
        private const string SEPARATOR = "\n";

        private const string DEVICE_GUID = "7e2006ab-88b5-4b09-b0b3-1ac3ca8da43e";

        private const string COMMAND_PING = "COMMAND:PING";
        private const string RESULT_PING = "RESULT:PING:OK:";

        private const string COMMAND_CALIBRATOR_GETBRIGHTNESS = "COMMAND:CALIBRATOR:GETBRIGHTNESS";
        private const string COMMAND_CALIBRATOR_ON = "COMMAND:CALIBRATOR:ON:";
        private const string COMMAND_CALIBRATOR_OFF = "COMMAND:CALIBRATOR:OFF";
        private const string RESULT_CALIBRATOR_BRIGHTNESS = "RESULT:CALIBRATOR:BRIGHTNESS:";

        private const string COMMAND_CALIBRATOR_GETSTATE = "COMMAND:CALIBRATOR:GETSTATE";
        private const string RESULT_CALIBRATOR_STATE = "RESULT:CALIBRATOR:STATE:";

        private const int MAX_BRIGHTNESS = 255;

        /// <summary>
        /// Initializes a new instance of the <see cref="CoverCalibrator"/> class.
        /// Must be public for COM registration.
        /// </summary>
        public CoverCalibrator()
        {
            tl = new TraceLogger("", "DarkSkyGeek");
            ReadProfile();
            tl.LogMessage("CoverCalibrator", "Starting initialization");
            connectedState = false;
            tl.LogMessage("CoverCalibrator", "Completed initialization");
        }

        //
        // PUBLIC COM INTERFACE ICoverCalibratorV1 IMPLEMENTATION
        //

        #region Common properties and methods.

        /// <summary>
        /// Displays the Setup Dialog form.
        /// If the user clicks the OK button to dismiss the form, then
        /// the new settings are saved, otherwise the old values are reloaded.
        /// THIS IS THE ONLY PLACE WHERE SHOWING USER INTERFACE IS ALLOWED!
        /// </summary>
        public void SetupDialog()
        {
            // consider only showing the setup dialog if not connected
            // or call a different dialog if connected
            if (IsConnected)
                System.Windows.Forms.MessageBox.Show("Already connected, just press OK");

            using (SetupDialogForm F = new SetupDialogForm(tl))
            {
                var result = F.ShowDialog();
                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    // Persist device configuration values to the ASCOM Profile store
                    WriteProfile();
                }
            }
        }

        public ArrayList SupportedActions
        {
            get
            {
                tl.LogMessage("SupportedActions Get", "Returning empty arraylist");
                return new ArrayList();
            }
        }

        public string Action(string actionName, string actionParameters)
        {
            LogMessage("", "Action {0}, parameters {1} not implemented", actionName, actionParameters);
            throw new ASCOM.ActionNotImplementedException("Action " + actionName + " is not implemented by this driver");
        }

        public void CommandBlind(string command, bool raw)
        {
            CheckConnected("CommandBlind");
            throw new ASCOM.MethodNotImplementedException("CommandBlind");
        }

        public bool CommandBool(string command, bool raw)
        {
            CheckConnected("CommandBool");
            throw new ASCOM.MethodNotImplementedException("CommandBool");
        }

        public string CommandString(string command, bool raw)
        {
            CheckConnected("CommandString");
            throw new ASCOM.MethodNotImplementedException("CommandString");
        }

        public void Dispose()
        {
            // Clean up the trace logger object
            tl.Enabled = false;
            tl.Dispose();
            tl = null;
        }

        public bool Connected
        {
            get
            {
                LogMessage("Connected", "Get {0}", IsConnected);
                return IsConnected;
            }
            set
            {
                tl.LogMessage("Connected", "Set {0}", value);
                if (value == IsConnected)
                    return;

                if (value)
                {
                    if (autoDetectComPort)
                    {
                        comPort = DetectCOMPort();
                    }

                    // Fallback, in case of detection error...
                    if (comPort == null)
                    {
                        comPort = comPortOverride;
                    }

                    if (!System.IO.Ports.SerialPort.GetPortNames().Contains(comPort))
                    {
                        throw new InvalidValueException("Invalid COM port", comPort.ToString(), String.Join(", ", System.IO.Ports.SerialPort.GetPortNames()));
                    }

                    LogMessage("Connected Set", "Connecting to port {0}", comPort);

                    objSerial = new Serial
                    {
                        Speed = SerialSpeed.ps57600,
                        PortName = comPort,
                        Connected = true
                    };

                    // Wait a second for the serial connection to establish
                    System.Threading.Thread.Sleep(1000);

                    objSerial.ClearBuffers();

                    // Poll the device (with a short timeout value) until successful,
                    // or until we've reached the retry count limit of 3...
                    objSerial.ReceiveTimeout = 1;
                    bool success = false;
                    for (int retries = 3; retries >= 0; retries--)
                    {
                        string response = "";
                        try
                        {
                            objSerial.Transmit(COMMAND_PING + SEPARATOR);
                            response = objSerial.ReceiveTerminated(SEPARATOR).Trim();
                        }
                        catch (Exception)
                        {
                            // PortInUse or Timeout exceptions may happen here!
                            // We ignore them.
                        }
                        if (response == RESULT_PING + DEVICE_GUID)
                        {
                            success = true;
                            break;
                        }
                    }

                    if (!success)
                    {
                        objSerial.Connected = false;
                        objSerial.Dispose();
                        objSerial = null;
                        throw new ASCOM.NotConnectedException("Failed to connect");
                    }

                    // Restore default timeout value...
                    objSerial.ReceiveTimeout = 10;

                    connectedState = true;
                }
                else
                {
                    CalibratorOff();

                    connectedState = false;

                    LogMessage("Connected Set", "Disconnecting from port {0}", comPort);

                    objSerial.Connected = false;
                    objSerial.Dispose();
                    objSerial = null;
                }
            }
        }

        public string Description
        {
            get
            {
                tl.LogMessage("Description Get", deviceName);
                return deviceName;
            }
        }

        public string DriverInfo
        {
            get
            {
                Version version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                string driverInfo = deviceName + " ASCOM Driver Version " + String.Format(CultureInfo.InvariantCulture, "{0}.{1}", version.Major, version.Minor);
                tl.LogMessage("DriverInfo Get", driverInfo);
                return driverInfo;
            }
        }

        public string DriverVersion
        {
            get
            {
                Version version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                string driverVersion = String.Format(CultureInfo.InvariantCulture, "{0}.{1}", version.Major, version.Minor);
                tl.LogMessage("DriverVersion Get", driverVersion);
                return driverVersion;
            }
        }

        public short InterfaceVersion
        {
            get
            {
                LogMessage("InterfaceVersion Get", "1");
                return Convert.ToInt16("1");
            }
        }

        public string Name
        {
            get
            {
                tl.LogMessage("Name Get", deviceName);
                return deviceName;
            }
        }

        #endregion

        #region ICoverCalibrator Implementation

        /// <summary>
        /// Returns the state of the device cover, if present, otherwise returns "NotPresent"
        /// </summary>
        public CoverStatus CoverState
        {
            get
            {
                return CoverStatus.NotPresent;
            }
        }

        /// <summary>
        /// Initiates cover opening if a cover is present
        /// </summary>
        public void OpenCover()
        {
            CheckConnected("OpenCover");
            throw new ASCOM.MethodNotImplementedException("OpenCover");
        }

        /// <summary>
        /// Initiates cover closing if a cover is present
        /// </summary>
        public void CloseCover()
        {
            CheckConnected("CloseCover");
            throw new ASCOM.MethodNotImplementedException("CloseCover");
        }

        /// <summary>
        /// Stops any cover movement that may be in progress if a cover is present and cover movement can be interrupted.
        /// </summary>
        public void HaltCover()
        {
            CheckConnected("HaltCover");
            throw new ASCOM.MethodNotImplementedException("HaltCover");
        }

        /// <summary>
        /// Returns the state of the calibration device, if present, otherwise returns "NotPresent"
        /// </summary>
        public CalibratorStatus CalibratorState
        {
            get
            {
                CheckConnected("CalibratorState");
                tl.LogMessage("CalibratorState", "Sending request to device to read calibrator state");
                objSerial.Transmit(COMMAND_CALIBRATOR_GETSTATE + SEPARATOR);
                tl.LogMessage("CalibratorState", "Sent request to device to read calibrator state");
                string response;
                try
                {
                    response = objSerial.ReceiveTerminated(SEPARATOR).Trim();
                }
                catch (Exception e)
                {
                    tl.LogMessage("CalibratorState", "Exception: " + e.Message);
                    throw e;
                }
                tl.LogMessage("CalibratorState", "Received response from device");
                if (!response.StartsWith(RESULT_CALIBRATOR_STATE))
                {
                    tl.LogMessage("CalibratorState", "Invalid response from device: " + response);
                    throw new ASCOM.DriverException("Invalid response from device: " + response);
                }
                string arg = response.Substring(RESULT_CALIBRATOR_STATE.Length);
                int value;
                try
                {
                    value = Int32.Parse(arg);
                }
                catch (FormatException)
                {
                    tl.LogMessage("CalibratorState", "Invalid state value received from device: " + arg);
                    throw new ASCOM.DriverException("Invalid state value received from device: " + arg);
                }
                if (value < 0 || value > 5)
                {
                    tl.LogMessage("CalibratorState", "Invalid state value received from device: " + arg);
                    throw new ASCOM.DriverException("Invalid state value received from device: " + arg);
                }
                else
                {
                    return (CalibratorStatus)value;
                }
            }
        }

        /// <summary>
        /// Returns the current calibrator brightness in the range 0 (completely off) to <see cref="MaxBrightness"/> (fully on)
        /// </summary>
        public int Brightness
        {
            get
            {
                CheckConnected("Brightness");
                tl.LogMessage("Brightness", "Sending request to device...");
                objSerial.Transmit(COMMAND_CALIBRATOR_GETBRIGHTNESS + SEPARATOR);
                tl.LogMessage("Brightness", "Waiting for response from device...");
                string response;
                try
                {
                    response = objSerial.ReceiveTerminated(SEPARATOR).Trim();
                }
                catch (Exception e)
                {
                    tl.LogMessage("Brightness", "Exception: " + e.Message);
                    throw e;
                }
                tl.LogMessage("Brightness", "Response from device: " + response);
                if (!response.StartsWith(RESULT_CALIBRATOR_BRIGHTNESS))
                {
                    tl.LogMessage("Brightness", "Invalid response from device: " + response);
                    throw new ASCOM.DriverException("Invalid response from device: " + response);
                }
                string arg = response.Substring(RESULT_CALIBRATOR_BRIGHTNESS.Length);
                int value;
                try
                {
                    value = Int32.Parse(arg);
                }
                catch (FormatException)
                {
                    tl.LogMessage("Brightness", "Invalid brightness value received from device: " + arg);
                    throw new ASCOM.DriverException("Invalid brightness value received from device: " + arg);
                }
                if (value < 0 || value > MAX_BRIGHTNESS)
                {
                    tl.LogMessage("Brightness", "Invalid brightness value received from device: " + arg);
                    throw new ASCOM.DriverException("Invalid brightness value received from device: " + arg);
                }
                return value;
            }
        }

        /// <summary>
        /// The Brightness value that makes the calibrator deliver its maximum illumination.
        /// </summary>
        public int MaxBrightness
        {
            get
            {
                return MAX_BRIGHTNESS;
            }
        }

        /// <summary>
        /// Turns the calibrator on at the specified brightness if the device has calibration capability
        /// </summary>
        /// <param name="Brightness"></param>
        public void CalibratorOn(int Brightness)
        {
            if (Brightness < 0 || Brightness > MAX_BRIGHTNESS)
            {
                throw new ASCOM.InvalidValueException("Invalid brightness value", Brightness.ToString(), "[0, " + MAX_BRIGHTNESS.ToString() + "]");
            }
            CheckConnected("CalibratorOn");
            tl.LogMessage("CalibratorOn", "Sending request to device...");
            objSerial.Transmit(COMMAND_CALIBRATOR_ON + Brightness + SEPARATOR);
            tl.LogMessage("CalibratorOn", "Request sent to device!");
        }

        /// <summary>
        /// Turns the calibrator off if the device has calibration capability
        /// </summary>
        public void CalibratorOff()
        {
            CheckConnected("CalibratorOff");
            tl.LogMessage("CalibratorOff", "Sending request to device...");
            objSerial.Transmit(COMMAND_CALIBRATOR_OFF + SEPARATOR);
            tl.LogMessage("CalibratorOff", "Request sent to device!");
        }

        #endregion

        #region Private properties and methods
        // here are some useful properties and methods that can be used as required
        // to help with driver development

        #region ASCOM Registration

        // Register or unregister driver for ASCOM. This is harmless if already
        // registered or unregistered. 
        //
        /// <summary>
        /// Register or unregister the driver with the ASCOM Platform.
        /// This is harmless if the driver is already registered/unregistered.
        /// </summary>
        /// <param name="bRegister">If <c>true</c>, registers the driver, otherwise unregisters it.</param>
        private static void RegUnregASCOM(bool bRegister)
        {
            using (var P = new ASCOM.Utilities.Profile())
            {
                P.DeviceType = "CoverCalibrator";
                if (bRegister)
                {
                    P.Register(driverID, deviceName);
                }
                else
                {
                    P.Unregister(driverID);
                }
            }
        }

        /// <summary>
        /// This function registers the driver with the ASCOM Chooser and
        /// is called automatically whenever this class is registered for COM Interop.
        /// </summary>
        /// <param name="t">Type of the class being registered, not used.</param>
        /// <remarks>
        /// This method typically runs in two distinct situations:
        /// <list type="numbered">
        /// <item>
        /// In Visual Studio, when the project is successfully built.
        /// For this to work correctly, the option <c>Register for COM Interop</c>
        /// must be enabled in the project settings.
        /// </item>
        /// <item>During setup, when the installer registers the assembly for COM Interop.</item>
        /// </list>
        /// This technique should mean that it is never necessary to manually register a driver with ASCOM.
        /// </remarks>
        [ComRegisterFunction]
        public static void RegisterASCOM(Type t)
        {
            RegUnregASCOM(true);
        }

        /// <summary>
        /// This function unregisters the driver from the ASCOM Chooser and
        /// is called automatically whenever this class is unregistered from COM Interop.
        /// </summary>
        /// <param name="t">Type of the class being registered, not used.</param>
        /// <remarks>
        /// This method typically runs in two distinct situations:
        /// <list type="numbered">
        /// <item>
        /// In Visual Studio, when the project is cleaned or prior to rebuilding.
        /// For this to work correctly, the option <c>Register for COM Interop</c>
        /// must be enabled in the project settings.
        /// </item>
        /// <item>During uninstall, when the installer unregisters the assembly from COM Interop.</item>
        /// </list>
        /// This technique should mean that it is never necessary to manually unregister a driver from ASCOM.
        /// </remarks>
        [ComUnregisterFunction]
        public static void UnregisterASCOM(Type t)
        {
            RegUnregASCOM(false);
        }

        #endregion

        /// <summary>
        /// Returns true if there is a valid connection to the driver hardware
        /// </summary>
        private bool IsConnected
        {
            get
            {
                return connectedState;
            }
        }

        /// <summary>
        /// Use this function to throw an exception if we aren't connected to the hardware
        /// </summary>
        /// <param name="message"></param>
        private void CheckConnected(string message)
        {
            if (!IsConnected)
            {
                throw new ASCOM.NotConnectedException(message);
            }
        }

        /// <summary>
        /// Read the device configuration from the ASCOM Profile store
        /// </summary>
        internal void ReadProfile()
        {
            using (Profile driverProfile = new Profile())
            {
                driverProfile.DeviceType = "CoverCalibrator";
                tl.Enabled = Convert.ToBoolean(driverProfile.GetValue(driverID, traceStateProfileName, string.Empty, traceStateDefault));
                autoDetectComPort = Convert.ToBoolean(driverProfile.GetValue(driverID, autoDetectComPortProfileName, string.Empty, autoDetectComPortDefault));
                comPortOverride = driverProfile.GetValue(driverID, comPortProfileName, string.Empty, comPortDefault);
            }
        }

        /// <summary>
        /// Write the device configuration to the  ASCOM  Profile store
        /// </summary>
        internal void WriteProfile()
        {
            using (Profile driverProfile = new Profile())
            {
                driverProfile.DeviceType = "CoverCalibrator";
                driverProfile.WriteValue(driverID, traceStateProfileName, tl.Enabled.ToString());
                driverProfile.WriteValue(driverID, autoDetectComPortProfileName, autoDetectComPortDefault.ToString());
                if (comPortOverride != null)
                {
                    driverProfile.WriteValue(driverID, comPortProfileName, comPortOverride.ToString());
                }
            }
        }

        internal string DetectCOMPort()
        {
            foreach (string portName in System.IO.Ports.SerialPort.GetPortNames())
            {
                Serial serial = null;

                try
                {
                    serial = new Serial
                    {
                        Speed = SerialSpeed.ps57600,
                        PortName = portName,
                        Connected = true,
                        ReceiveTimeout = 1
                    };
                }
                catch (Exception)
                {
                    // If trying to connect to a port that is already in use, an exception will be thrown.
                    continue;
                }

                // Wait a second for the serial connection to establish
                System.Threading.Thread.Sleep(1000);

                serial.ClearBuffers();

                // Poll the device (with a short timeout value) until successful,
                // or until we've reached the retry count limit of 3...
                bool success = false;
                for (int retries = 3; retries >= 0; retries--)
                {
                    string response = "";
                    try
                    {
                        serial.Transmit(COMMAND_PING + SEPARATOR);
                        response = serial.ReceiveTerminated(SEPARATOR).Trim();
                    }
                    catch (Exception)
                    {
                        // PortInUse or Timeout exceptions may happen here!
                        // We ignore them.
                    }
                    if (response == RESULT_PING + DEVICE_GUID)
                    {
                        success = true;
                        break;
                    }
                }

                serial.Connected = false;
                serial.Dispose();

                if (success)
                {
                    return portName;
                }
            }

            return null;
        }

        /// <summary>
        /// Log helper function that takes formatted strings and arguments
        /// </summary>
        /// <param name="identifier"></param>
        /// <param name="message"></param>
        /// <param name="args"></param>
        internal void LogMessage(string identifier, string message, params object[] args)
        {
            var msg = string.Format(message, args);
            tl.LogMessage(identifier, msg);
        }
        #endregion
    }
}
