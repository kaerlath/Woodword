# Publishing Woodword

## Create the GitHub repository

1. Sign in to GitHub and select **New repository**.
2. Name it `Woodword` and add a short description.
3. Choose **Public**.
4. Do not add a README, `.gitignore`, or license in GitHub; they already exist here.
5. Create the repository and copy the HTTPS repository address GitHub shows you.

## Connect this local project

From the folder containing this file, run:

```powershell
git init
git add .
git commit -m "Initial Woodword release"
git branch -M main
git remote add origin https://github.com/YOUR-GITHUB-NAME/Woodword.git
git push -u origin main
```

Replace `YOUR-GITHUB-NAME` with the account or organization that owns the repository.

## Add the relay token securely

1. Open the repository on GitHub.
2. Select **Settings**.
3. Open **Secrets and variables**, then **Actions**.
4. Select **New repository secret**.
5. Name it exactly `WOODWORD_RELAY_TOKEN`.
6. Paste the value used by the Worker's `WOODWORD_CLIENT_TOKEN` secret and save it.

The value is injected into release builds by GitHub Actions. It is not committed to source. It will, however, be present inside the distributed DLL and must be treated as an application token rather than an unrecoverable secret.

## Publish a release

Create and push a version tag:

```powershell
git tag -a v0.2.0 -m "Woodword v0.2.0"
git push origin v0.2.0
```

The **Publish release** workflow will build Woodword, package it, and create the GitHub release automatically. Follow its progress under the repository's **Actions** tab. When it completes, the release and downloadable plugin ZIP appear under **Releases**. Keep `repo.json` synchronized with the release version and asset URL.

## Before each later release

Update the version in `Woodword/Woodword.csproj`, commit and push the changes, then create a matching new tag such as `v0.2.0`.
