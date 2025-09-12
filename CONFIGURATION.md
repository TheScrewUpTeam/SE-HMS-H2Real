# Configuration – H2Real

This file documents all configurable parameters for **H2Real**, an expansion module for the [Heat Management System](https://github.com/TheScrewUpTeam/SE-Heat-Management).  
These values can be adjusted to fine-tune performance, efficiency, and difficulty.  

---

## Parameters

| Parameter | Default Value | Unit | Description |
|-----------|---------------|------|-------------|
| `SYSTEM_AUTO_UPDATE` | `true` | – | If enabled, the system automatically updates values when changes are detected. |
| `ICE_MELTING_ENERGY_PER_KG` | `334000` | W/kg | Energy required to melt 1 kg of ice. Based on the latent heat of fusion of water. |
| `GAS_COMPRESSION_POWER_FULL_PER_LITER` | `500` | W/L | Power required to compress 1 liter of hydrogen gas to full storage pressure. |
| `ENERGY_PER_LITER` | `1495.0` | J/L | Energy density of hydrogen gas per liter (at storage conditions). |
| `H2_THRUST_EFFICIENCY` | `0.65` | – | Efficiency of hydrogen thrusters when converting fuel energy into thrust. |
| `H2_ENGINE_EFFICIENCY` | `0.65` | – | Efficiency of hydrogen engines (ICE equivalent). Represents typical combustion engine efficiency. |
| `H2_ENGINE_CRITICAL_TEMP` | `300` | °C | Critical temperature for hydrogen engines. Beyond this, overheating effects occur. |
| `H2_THRUSTER_CRITICAL_TEMP` | `500` | °C | Critical temperature for hydrogen thrusters. Beyond this, overheating effects occur. |
| `DAMAGE_PERCENT_ON_VERHEAT` | `0.2` | % | Fraction of block integrity lost per overheating event. |


---

## Notes
- All values are based on scientific references but tuned for balanced gameplay.  
- You can edit these parameters in the config file before starting the game or server.  
- Some values are tightly coupled with Heat Management System behavior.  
