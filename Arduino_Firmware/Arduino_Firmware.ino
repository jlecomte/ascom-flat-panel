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

#define MIN_BRIGHTNESS 0
#define MAX_BRIGHTNESS 255
#define PWM_FREQ 20000

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

    // Make sure the RX, TX, and built-in LEDs don't turn on, they are very bright!
    // Even though the board is inside an enclosure, the light can be seen shining
    // through the small opening for the USB connector! Unfortunately, it is not
    // possible to turn off the power LED (green) in code...
    pinMode(PIN_LED_TXL, INPUT);
    pinMode(PIN_LED_RXL, INPUT);
    pinMode(LED_BUILTIN, OUTPUT);
    digitalWrite(LED_BUILTIN, HIGH);

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

void setBrightness() {
    // This only works on Seeeduino Xiao.
    // The `pwm` function is defined in the following file:
    // %localappdata%\Arduino15\packages\Seeeduino\hardware\samd\1.8.2\cores\arduino\wiring_pwm.cpp
    // For other Arduino-compatible boards, consider using:
    //   analogWrite(ledPin, brightness);
    // The nice thing about the `pwm` function is that we can set the frequency
    // to a much higher value (I use 20kHz) This does not work on all pins!
    // For example, it does not work on pin 7 of the Xiao, but it works on pin 8.
    int value = map(brightness, MIN_BRIGHTNESS, MAX_BRIGHTNESS, 0, 1023);
    pwm(ledPin, PWM_FREQ, value);
}

void calibratorOn(byte _brightness) {
    brightness = _brightness;
    setBrightness();
}

void calibratorOff() {
    brightness = 0;
    setBrightness();
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
