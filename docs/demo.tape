# PassGen demo - records the Symbolic Intent Architecture trace as a GIF.
#
# Render:   vhs docs/demo.tape         (writes docs/demo.gif)
# Install:  https://github.com/charmbracelet/vhs
# Prereq:   a `passgen` on PATH, or edit the two commands below to point at
#           publish/app/passgen.exe (run .\make-publish.ps1 first).

Output docs/demo.gif

Set Shell "bash"
Set FontSize 15
Set Width 1180
Set Height 900
Set Padding 24
Set Theme "Catppuccin Mocha"
Set TypingSpeed 35ms

# --- Scenario 1: success (language -> verified intent) ---
Type "passgen --trace 'give me a 20-character password with 3 numbers, 2 symbols, no confusing characters'"
Sleep 700ms
Enter
Sleep 3500ms

# --- Scenario 2: fail-closed (impossible request is REJECTED before the tool runs) ---
Type "passgen --trace 'make me a 4-character password with 10 uppercase letters'"
Sleep 700ms
Enter
Sleep 4000ms
