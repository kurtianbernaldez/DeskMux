# Deploying deskmux.kurtian.dev

The website workflow builds this folder and publishes `dist/client` to GitHub Pages whenever `main` changes.

After the `kurtian/DeskMux` repository is public and the source has been pushed:

1. Open **Repository Settings → Pages** and choose **GitHub Actions** as the source.
2. In the same Pages screen, set the custom domain to `deskmux.kurtian.dev`.
3. At the DNS provider for `kurtian.dev`, create a `CNAME` record named `deskmux` pointing to `kurtian.github.io`.
4. Wait for GitHub's DNS check and certificate provisioning, then enable **Enforce HTTPS**.
5. Run the Website workflow manually if the first push did not trigger it.

The download links become live after the first tagged GitHub release creates `DeskMux-Setup-x64.exe` and `DeskMux-portable-x64.zip`.
