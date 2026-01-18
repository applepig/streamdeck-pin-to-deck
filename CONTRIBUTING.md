# Contributing to Pin to Deck

Thank you for your interest in contributing to Pin to Deck! We welcome contributions from the community.

## Getting Started

1.  **Fork the repository** on GitHub.
2.  **Clone your fork** locally.
3.  **Setup the environment**:
    ```powershell
    cd streamdeck-pin-to-deck
    .\dev.ps1 install
    ```

## Development Workflow

We use a PowerShell script `dev.ps1` to manage the development lifecycle.

-   **Build**: `.\dev.ps1 build`
-   **Test**: `.\dev.ps1 test`
-   **Deploy**: `.\dev.ps1 deploy` (deploys to local Stream Deck for testing)
-   **Watch**: `.\dev.ps1 watch` (hot reload for build)

## Pull Request Process

1.  Create a new branch for your feature or bugfix (`git checkout -b feature/amazing-feature`).
2.  Make your changes.
3.  Ensure all tests pass (`.\dev.ps1 test`).
4.  Commit your changes using clear commit messages.
5.  Push to your fork and submit a Pull Request.

## Coding Standards

-   Use standard C# coding conventions.
-   Run tests before submitting a PR.
-   Keep PRs focused on a single feature or fix.

## License

By contributing, you agree that your contributions will be licensed under the MIT License.
