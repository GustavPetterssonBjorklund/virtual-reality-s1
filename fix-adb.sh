set -euo pipefail

adb kill-server
adb start-server
adb devices
