# Coding standards

## Porting from Ruby

- **Ruby truthiness.** Ruby treats only `nil` and `false` as false; `""`, `0`
  and `[]` are true. Port an `if x` guard as the check Ruby makes
  (`x is not null`), not as `x.Length > 0` or `!string.IsNullOrEmpty(x)`. A
  value from `capture` is a string, never `nil`, so upstream's `if target` on it
  always passes. A guard that is wrong on purpose is a deviation.
- **Deviations.** Mark every site where ported code departs from upstream on
  purpose with a `// DEVIATION:` comment that says what upstream does — each
  changed guard or call the deviation touches as well as any new helper. The
  README's known deviations list names the behaviour once.
