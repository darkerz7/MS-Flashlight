# MS-Flashlight for ModSharp
Flashlight for Counter-Strike 2

## Features:
1. Switch to F
2. Transmit from other players

## Required packages:
1. [ModSharp](https://github.com/Kxnrl/modsharp-public)

## Installation:
0. Install GameEventManager to `sharp/shared` folger
1. Compile or copy MS-Flashlight to `sharp/modules/MS-Flashlight` folger
2. Copy FlashLight.json to `sharp/locales` folger

## Commands:
Client Command | Description
--- | ---
`ms_fl_color [R G B]` | Allows the player to change the color of the flashlight (default: 255 255 255; min 0; max 255)
`ms_fl_rainbow` | Toggle the flashlight glow mode to rainbow