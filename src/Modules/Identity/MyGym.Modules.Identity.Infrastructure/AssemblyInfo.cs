using System.Runtime.CompilerServices;

// Unrun-tests finding #3: RefreshTokenPersistenceTests must call the real
// RefreshTokenRepository.FindActiveAsync rather than hand-rolling its predicate inline, which
// proved nothing about the production query path. RefreshTokenRepository is internal by design
// (module Infrastructure types are not part of any published contract); this grants the one test
// assembly that needs to construct it directly.
[assembly: InternalsVisibleTo("MyGym.IntegrationTests")]
