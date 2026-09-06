# Deploying deskmux.kurtian.dev with Coolify

The website is an independent static container. DeskMux itself remains a Windows application distributed through GitHub Releases.

## Coolify setup

1. Create a new **Application** in Coolify and connect the Git repository containing this website.
2. Choose the **Dockerfile** build pack.
3. If the website stays in the DeskMux repository, set the base directory to `/website` and the Dockerfile location to `/Dockerfile`. If it is moved into its own repository, use `/` for both.
4. Set the exposed container port to `80`.
5. Add `https://deskmux.kurtian.dev` as the domain.
6. Configure the `deskmux` DNS record to point to the VPS address, then deploy. Coolify will request and renew HTTPS after DNS reaches the server.
7. Set the health-check path to `/` with expected status `200`. The Docker image also includes its own health check.

No environment variables, database, persistent volume, or Node.js runtime are needed after the image is built. New pushes can trigger automatic Coolify deployments through its Git integration or webhook.

The download links become live after the first tagged GitHub release creates `DeskMux-Setup-x64.exe` and `DeskMux-portable-x64.zip`.
