// Operazioni lunghe (importazione, estrazione da un documento): al primo invio il pulsante si spegne e dice che cosa sta
// succedendo, così non si invia due volte lo stesso modulo.
document.addEventListener('submit', event => {
    const form = event.target;

    if (!(form instanceof HTMLFormElement) || form.hasAttribute('data-agent-stream')) {
        return;
    }

    const button = event.submitter;

    if (!(button instanceof HTMLButtonElement) || !button.dataset.busyText) {
        return;
    }

    // Lo spegnimento avviene dopo l'invio: un pulsante spento prima non manderebbe il proprio nome e valore.
    setTimeout(() => {
        button.disabled = true;
        button.textContent = button.dataset.busyText;
    }, 0);
});
