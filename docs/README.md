# Documentation

End-user guides for version 0.21.0:

- [English user guide](USER_GUIDE.md)
- [Guide utilisateur français](GUIDE_UTILISATEUR.md)
- [Community language packs and contributor format](LANGUAGE_PACKS.md)
- [Offline release start page](../dist/START-HERE.html)

`scripts/publish-docs.ps1` converts the two guide sources to self-contained HTML. The normal build includes these pages in its output directory.

Developer references:

- [Languages and community packs in 0.21](RELEASE_0.21.md)
- [Theme, checkbox fixes, server exports and FTP in 0.20](RELEASE_0.20.md)
- [Dedicated-server Java support and validation scope](SERVER_JAVA_0.19.md)
- [Java Loader API](JAVA_LOADER.md)
- [Proposed runtime network contract](RUNTIME_NETWORK_CONTRACT.md) — design work, not an implemented login protocol.
- [Java mod porting studies](java-mods/README.md)

Other versioned notes record earlier implementation work and tests. Their historical paths, release sizes and report references are not current delivery instructions. Generated reports and screenshots are outside the end-user delivery and can be removed with `scripts/clean-generated.ps1`; test source and scripts remain in the project. Use the repository README and the end-user guides for the current release.
