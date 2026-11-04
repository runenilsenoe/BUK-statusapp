// Helpers for the pot screen (Components/Pages/Pot.razor).

export function playSound(url) {
    const audio = new Audio(url);
    audio.volume = 0.5;
    // Browsers block sound until the page has been clicked; the "Lyd" switch is that click.
    audio.play().catch(() => { });
}

export async function setFullscreen(on) {
    try {
        if (on && !document.fullscreenElement) {
            await document.documentElement.requestFullscreen();
        } else if (!on && document.fullscreenElement) {
            await document.exitFullscreen();
        }
    } catch {
        // Not allowed (e.g. in an iframe); the page still switches to its big-screen layout.
    }
}
