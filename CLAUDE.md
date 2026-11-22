# Working rules

- Prefix every shell command with `rtk`.
- Cut a branch before writing code. `main` advances only through a GitHub PR, merged with squash.
- Test runs, commits and pushes go to a Haiku subagent. It runs the suite and commits only on `Failed: 0`.
- Code review runs on a fresh Sonnet 5.5 subagent, read-only, once per mandate.
- One type per `.cs` file.
- Less is more:
  - at most 150 net production lines per mandate;
  - an established method or library before hand-written code;
  - no abstraction before two real uses;
  - no configuration option nobody asked for.
- Game logs contain the player's name. Never commit a log unless the name has been replaced.

## Commands

```
dotnet build InventoryMovementTracker.slnx
dotnet test  InventoryMovementTracker.slnx
```
