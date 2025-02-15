# UAA Solution - Automation Control System

## Overview

UAA Solution is a comprehensive automation control system built with WPF (.NET) that manages complex motion control, sensor monitoring, and precision automation tasks. The system integrates multiple hexapods, gantry systems, and various sensors to provide high-precision control and monitoring capabilities.

## Key Features

### Motion Control
- **Multi-Axis Control**: Manages multiple hexapods (Left, Right, Bottom) and gantry systems
- **Path Planning**: Intelligent motion path planning with safety checks and collision avoidance
- **Position Teaching**: Interactive position teaching and management system
- **Coordinated Movement**: Synchronized movement of multiple devices

### Sensor Integration
- **Real-time Monitoring**: Continuous monitoring of various sensors including:
  - Keithley Current Measurements
  - Power Meters
  - Position Sensors
  - Analog Inputs
- **Data Visualization**: Real-time display of sensor readings with trend analysis

### Hardware Integration
- **Hexapod Control**: Complete control of PI hexapod systems
- **Gantry Systems**: ACS gantry control integration
- **I/O Management**: Digital and analog I/O handling through EZIIO devices
- **Camera Integration**: Basler camera integration for visual feedback

### Safety Features
- Motion safety limits and collision prevention
- Emergency stop functionality
- Comprehensive error handling and logging
- Position validation and verification

## System Architecture

### Core Components
1. **Motion Control System**
   - HexapodConnectionManager
   - GantryConnectionManager
   - MotionGraphManager
   - Position Registry

2. **Sensor Management**
   - RealTimeDataManager
   - DataCollection System
   - Sensor Calibration

3. **User Interface**
   - Motion Control Panels
   - Sensor Monitoring Displays
   - Position Teaching Interface
   - Camera Display

### Key Technologies
- WPF (.NET) for the user interface
- Serilog for comprehensive logging
- JSON configuration system
- Graph-based motion planning
- Real-time data processing

## Configuration System
- Device configurations in JSON format
- Configurable motion paths and positions
- I/O mapping and configuration
- Sensor calibration settings

## Safety and Error Handling
- Comprehensive logging system
- Motion safety boundaries
- Device status monitoring
- Error recovery procedures

## Development Setup

### Prerequisites
- Visual Studio 2022 or later
- .NET Framework 4.8 or later
- Required device drivers:
  - PI Hexapod Controller
  - ACS Motion Controller
  - Basler Camera SDK
  - Keithley Instruments Drivers

### Configuration Files
- `WorkingPositions.json`: Device position configurations
- `WorkingGraphs.json`: Motion path definitions
- `IOConfig.json`: I/O device configurations
- `channelconfig.json`: Sensor channel configurations

### Building the Project
1. Clone the repository
2. Open the solution in Visual Studio
3. Restore NuGet packages
4. Build the solution

## Contributing
Please read [CONTRIBUTING.md](CONTRIBUTING.md) for details on our code of conduct and the process for submitting pull requests.

## License
This project is licensed under the MIT License - see the [LICENSE.md](LICENSE.md) file for details.

## Acknowledgments
- PI (Physik Instrumente) for hexapod control systems
- ACS Motion Control for gantry systems
- Basler for camera integration
- Keithley Instruments for precision measurements

## Support
For support and questions, please create an issue in the GitHub repository or contact the development team.

## Project Status
Active development - Regular updates and improvements

---
*Note: This README is maintained by the development team. For the most up-to-date information, please check the documentation or contact the team directly.*
