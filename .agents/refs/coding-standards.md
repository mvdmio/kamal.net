# Coding standards

## Porting from Ruby

- **Ruby truthiness.** Ruby treats only `nil` and `false` as false; `""`, `0`
  and `[]` are true. Port an `if x` guard as the check Ruby makes
  (`x is not null`), not as `x.Length > 0` or `!string.IsNullOrEmpty(x)`. A
  value from `capture` is a string, never `nil`, so upstream's `if target` on it
  always passes. Where the faithful guard is wrong on purpose, say so in a code
  comment and in the README's known deviations list.
