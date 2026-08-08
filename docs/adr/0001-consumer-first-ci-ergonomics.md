# Consumer-first CI ergonomics over Ruby surface parity

Kamal.NET is a port of Ruby Kamal, but suite-style GitHub Actions consumers hit gaps the port’s “keys only” SSH path and lack of ERB force into every repo. We will ship CI and config ergonomics that fix those consumers even when the surface has no Ruby twin (env-key convenience, limited config expansion, GitHub Actions, public failure classes and exit codes). Matching Ruby remains useful when it already solves the pain (for example ssh-agent); it is not a gate on shipping a better .NET CI path.
