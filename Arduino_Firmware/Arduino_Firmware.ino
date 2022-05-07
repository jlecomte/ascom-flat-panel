/*
 * Arduino_Firmware.ino
 * Copyright (C) 2022 - Present, Julien Lecomte - All Rights Reserved
 * Licensed under the MIT License. See the accompanying LICENSE file for terms.
 */

constexpr auto DEVICE_GUID = "7e2006ab-88b5-4b09-b0b3-1ac3ca8da43e";

constexpr auto COMMAND_PING = "COMMAND:PING";
constexpr auto RESULT_PING = "RESULT:PING:OK:";

constexpr auto COMMAND_INFO = "COMMAND:INFO";
constexpr auto RESULT_INFO = "RESULT:INFO:DarkSkyGeek's Flat Panel Firmware v1.0";

constexpr auto COMMAND_CALIBRATOR_GETBRIGHTNESS = "COMMAND:CALIBRATOR:GETBRIGHTNESS";
constexpr auto COMMAND_CALIBRATOR_ON = "COMMAND:CALIBRATOR:ON:";
constexpr auto COMMAND_CALIBRATOR_OFF = "COMMAND:CALIBRATOR:OFF";
constexpr auto RESULT_CALIBRATOR_BRIGHTNESS = "RESULT:CALIBRATOR:BRIGHTNESS:";

constexpr auto ERROR_INVALID_COMMAND = "ERROR:INVALID_COMMAND";

byte brightness = 0;

int ledPin = 8;

// The `setup` function runs once when you press reset or power the board.
void setup() {
    // Initialize serial port I/O.
    Serial.begin(57600);
    while (!Serial) {
        ; // Wait for serial port to connect. Required for native USB!
    }
    Serial.flush();

    // Setup LED pin as output
    pinMode(ledPin, OUTPUT);
}

// The `loop` function runs over and over again until power down or reset.
void loop() {
    if (Serial.available() > 0) {
        String command = Serial.readStringUntil('\n');
        if (command == COMMAND_PING) {
            handlePing();
        }
        else if (command == COMMAND_INFO) {
            sendFirmwareInfo();
        }
        else if (command == COMMAND_CALIBRATOR_GETBRIGHTNESS) {
            sendCalibratorBrightness();
        }
        else if (command.startsWith(COMMAND_CALIBRATOR_ON)) {
            String arg = command.substring(strlen(COMMAND_CALIBRATOR_ON));
            byte value = (byte) arg.toInt();
            calibratorOn(value);
        }
        else if (command == COMMAND_CALIBRATOR_OFF) {
            calibratorOff();
        }
        else {
            handleInvalidCommand();
        }
    }
}

//-- CALIBRATOR HANDLING ------------------------------------------------------

void sendCalibratorBrightness() {
    Serial.print(RESULT_CALIBRATOR_BRIGHTNESS);
    Serial.println(brightness);
}

void calibratorOn(byte _brightness) {
    brightness = _brightness;
    analogWrite(ledPin, brightness);
}

void calibratorOff() {
    brightness = 0;
    analogWrite(ledPin, brightness);
}

//-- MISCELLANEOUS ------------------------------------------------------------

void handlePing() {
    Serial.print(RESULT_PING);
    Serial.println(DEVICE_GUID);
}

void sendFirmwareInfo() {
    Serial.println(RESULT_INFO);
}

void handleInvalidCommand() {
    Serial.println(ERROR_INVALID_COMMAND);
}
