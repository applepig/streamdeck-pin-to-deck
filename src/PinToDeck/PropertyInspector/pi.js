// pi.js - Property Inspector Logic
// Build: __BUILD_TIMESTAMP__

/**
 * Log message to debug console
 */
function log(msg) {
    const consoleDiv = document.getElementById('debug-console');
    if (consoleDiv) {
        const timestamp = new Date().toLocaleTimeString();
        consoleDiv.innerHTML += `<div>${timestamp} ${msg}</div>`;
        consoleDiv.scrollTop = consoleDiv.scrollHeight;
    }
    console.log(`[PI] ${msg}`);
}

/**
 * Show build info in the debug panel
 */
function showBuildInfo() {
    const buildInfo = document.getElementById('build-info');
    if (buildInfo) {
        const buildTime = '__BUILD_TIMESTAMP__';
        buildInfo.innerHTML = `Build: ${buildTime} | v0.1.0`;
    }
    log('PI Initialized');
}

function updateSections(type) {
    const selectSection = document.getElementById('select-section');
    const customSection = document.getElementById('custom-section');

    if (type === 'custom') {
        if (selectSection) selectSection.style.display = 'none';
        if (customSection) customSection.style.display = 'block';
    } else {
        if (selectSection) selectSection.style.display = 'block';
        if (customSection) customSection.style.display = 'none';
    }
}

/**
 * Update show overlay checkbox state based on window cycling action
 */
function updateShowOverlayState() {
    const cyclingSelect = document.querySelector('sdpi-select[setting="window_cycling_action"]');
    const overlayCheckbox = document.querySelector('sdpi-checkbox[setting="enable_show_overlay"]');

    if (cyclingSelect && overlayCheckbox) {
        const action = cyclingSelect.value || '0';

        if (action === '2') {
            // Disable overlay checkbox when action is "Do nothing"
            overlayCheckbox.setAttribute('disabled', '');
            log('Show overlay disabled (cycling action is "Do nothing")');
        } else {
            // Enable overlay checkbox for other actions
            overlayCheckbox.removeAttribute('disabled');
            log(`Show overlay enabled (cycling action is "${action}")`);
        }
    }
}

// Initialize when DOM is ready
document.addEventListener('DOMContentLoaded', () => {
    showBuildInfo();

    const typeSelector = document.getElementById('app-type-selector');
    if (typeSelector) {
        // Initial state
        updateSections(typeSelector.value);

        // Listen for changes
        typeSelector.addEventListener('change', (e) => {
            log(`App Type changed to: ${e.target.value}`);
            updateSections(e.target.value);
        });

        // Also listen for immediate input if supported
        typeSelector.addEventListener('input', (e) => {
            updateSections(e.target.value);
        });

        // Watch for late value updates (settings loaded)
        // SDPI sometimes updates value after DOMContentLoaded
        const observer = new MutationObserver((mutations) => {
            mutations.forEach((mutation) => {
                if (mutation.type === 'attributes' && mutation.attributeName === 'value') {
                    log(`App Type value updated via attribute: ${typeSelector.value}`);
                    updateSections(typeSelector.value);
                }
            });
        });
        observer.observe(typeSelector, { attributes: true });
    }

    // Handle window cycling action changes
    const cyclingSelect = document.querySelector('sdpi-select[setting="window_cycling_action"]');
    if (cyclingSelect) {
        // Initial state
        updateShowOverlayState();

        // Listen for changes
        cyclingSelect.addEventListener('change', () => {
            log(`Window cycling action changed to: ${cyclingSelect.value}`);
            updateShowOverlayState();
        });

        // Watch for attribute changes (when settings are loaded)
        const cyclingObserver = new MutationObserver(() => {
            updateShowOverlayState();
        });
        cyclingObserver.observe(cyclingSelect, { attributes: true });
    }
});

// Listen for global errors
window.addEventListener('error', (e) => {
    log(`ERROR: ${e.message}`);
});
