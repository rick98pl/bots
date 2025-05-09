# RealeraDX Bot for Tibia

![Tibia Bot](https://i.imgur.com/placeholder.png)

An advanced automation tool for Tibia, specifically designed for RealeraDX client.

## 🌟 Features

### Core Functionality
- **Auto-Potion System**: Automatically uses health and mana potions when levels drop below configurable thresholds
- **Path Recording & Playback**: Record and save walking paths, then automatically follow them for efficient navigation
- **Waypoint Randomization**: Small random variations in pathing to appear more human-like and avoid detection
- **Combat Management**: Automatically switches to combat mode when monsters appear
- **Outfit Maintenance**: Maintains your desired outfit ID, preventing unwanted appearance changes
- **Real-time Stats Display**: Overlay showing HP, mana, position, target information and other essential data
- **Stuck Detection & Recovery**: Identifies when character gets stuck and uses alternative movement methods

### Advanced Hunting Features
- **Target Blacklisting**: Temporarily blacklists monsters that are too far away or unreachable
- **Automatic Looting**: Recognizes valuable items in loot and automatically sorts them to inventory
- **Platinum Management**: Special handling for platinum coins, including right-clicking to convert
- **Combat Rotation**: Automatically executes optimal combat skills in the right sequence
- **Click-Around-Corpse**: Automatically loots corpses after killing monsters 
- **Chase Path Tracking**: Returns to the original path after chasing monsters
- **Ring Toggling**: Automatically equips/unequips rings during combat

### Safety Features
- **Spawn Detection**: Visual spawn detection system with configurable templates
- **Color Change Detection**: Alerts for unusual colors that might indicate danger
- **Distance Warning**: Triggers alerts when too far from waypoints
- **Emergency Shutdown**: Immediate program termination with emergency key sequences if dangerous situations detected
- **Anti-Detection Mechanisms**: Varied timing and movement patterns to appear more human

### Visual Debugging
- **Click Visualization**: Shows clicks on screen with countdown timers
- **Waypoint Markers**: Visual indicators for path waypoints
- **Action Overlay**: Transparent overlay showing HP/MP, position, and other game information
- **Debug Logging**: Comprehensive logging of all actions for troubleshooting

## 🚀 Getting Started

### Prerequisites
- Windows operating system
- .NET Framework 4.7+
- RealeraDX Tibia client
- Emgu CV (included in the release)
- System.Drawing and other standard .NET libraries

### Installation
1. Download the latest release from the [Releases](https://github.com/username/realeradx-bot/releases) page
2. Extract all files to a folder of your choice
3. Run the application as Administrator

### Initial Setup
1. Start RealeraDX Tibia client and log into your character
2. Launch the bot - it will automatically detect the Tibia window
3. If multiple client windows are found, select the appropriate one

## 📋 Usage Guide

### Basic Controls
- `R` - Start/Stop path recording
- `P` - Start/Stop path playback
- `A` - Toggle auto-potions
- `O` - Toggle overlay display
- `W` - Toggle spawn watcher
- `F/G` - Decrease/Increase outfit by 1
- `H/J` - Decrease/Increase outfit by 10
- `S` - Stop alarm sounds
- `Q` - Quit application

### Setting Up Auto-Potion
The bot uses function keys for potions by default:
- F1 for Health Potions (triggers at 50% HP)
- F2 for Mana Potions (triggers at 70% Mana)

### Recording Paths
1. Navigate to your desired starting point
2. Press `R` to start recording
3. Walk the path you want to record
4. Press `R` again to stop recording and save

### Item Recognition Setup
1. Create a folder named `drops` in the bot directory
2. Add PNG/JPG images of items you want the bot to recognize
3. Name platinum template as "100platinum" for special handling
4. Name gold coin template as "100gold" for special handling

### Spawn Detection Setup
1. Create a folder named `images` in the bot directory
2. Add PNG/JPG images of monsters/spawns you want detected
3. (Optional) Create a priorities.txt file to set detection priorities

## ⚠️ Important Safety Notes

- **Use at Your Own Risk**: Botting violates Tibia's rules and may result in your account being banned
- **Start Small**: When testing, use short paths and supervised operation before extended use
- **Test in Safe Areas**: Initially test in safer hunting grounds with fewer players
- **Active Monitoring**: Do not leave the bot completely unattended
- **Account Security**: Never share your credentials or bot settings with others

## 🔧 Troubleshooting

### Common Issues
- **Memory Reading Errors**: Restart the bot after client updates
- **Path Playback Issues**: Make sure the bot is using the correct waypoints file
- **Click Recognition Problems**: Adjust the UI position variables for your screen resolution
- **Spawn Detection Not Working**: Check image templates are clear and properly sized
- **Performance Issues**: Try disabling click overlay to reduce CPU usage

### Config Adjustments
- Adjust `DEFAULT_HP_THRESHOLD` for health potion usage point
- Adjust `DEFAULT_MANA_THRESHOLD` for mana potion usage point
- Modify `statsOverlayRightOffset` and `statsOverlayBottomOffset` to position the overlay correctly
- Set `lootMatchThreshold` value to adjust item recognition sensitivity

## 🛠 Development

The bot is written in C# and uses several key libraries:
- **System.Diagnostics**: For process management
- **System.Runtime.InteropServices**: For Win32 API calls
- **System.Drawing**: For UI and visuals
- **Emgu CV**: For computer vision and image recognition

Key components:
- Memory reading for game state
- Windows message system for input simulation
- Template matching for object recognition
- Path finding and movement logic

## 📝 License

This project is licensed under the MIT License - see the LICENSE file for details.

## 📞 Support

For support and discussions, please [open an issue](https://github.com/username/realeradx-bot/issues) on the GitHub repository.

---

**Disclaimer**: This tool is for educational purposes only. Use of bots in Tibia is against the game's rules and may result in penalties including account bans. The developers are not responsible for any consequences resulting from the use of this software.
