// Avvio di un agente da una pagina: il modulo viene inviato come JSON all'indirizzo di data-agent-stream (un endpoint della
// Web, che aggiunge la chiave del ruolo e inoltra lo stream del servizio Agents). La risposta è uno stream SSE: eventi "step",
// poi "result" oppure "error". Tutto ciò che arriva dal modello si scrive come testo, mai come HTML.
(() => {
    const storageKey = 'trasporto-agent-result:' + location.pathname;

    function element(tag, className, text) {
        const node = document.createElement(tag);

        if (className) {
            node.className = className;
        }

        if (text !== undefined && text !== null) {
            node.textContent = text;
        }

        return node;
    }

    function seconds(milliseconds) {
        return (milliseconds / 1000).toLocaleString('it-IT', { minimumFractionDigits: 1, maximumFractionDigits: 1 }) + ' s';
    }

    function modelLine(result) {
        const model = result.model;
        const tokens = model.inputTokens === null || model.inputTokens === undefined ? '' : ` · ${model.inputTokens} + ${model.outputTokens ?? 0} token`;

        return `${model.provider}/${model.model} · ${seconds(model.elapsedMs)}${tokens} · ${result.toolCalls.length} chiamate ai tool`;
    }

    function warnings(target, result) {
        for (const warning of result.warnings ?? []) {
            target.append(element('div', 'alert alert-warning py-2 small mb-2', warning));
        }
    }

    function renderMapping(target, result) {
        const created = result.proposals.filter(proposal => proposal.created).length;
        const existing = result.proposals.length - created;

        target.append(element('div', 'alert alert-success py-2 mb-2',
            `Ultimo run dell'agente: ${created} proposte nuove` + (existing > 0 ? `, ${existing} già in attesa` : '') +
            `, ${result.discarded.length} tentativi scartati dal codice.`));

        if (result.discarded.length > 0) {
            const list = element('ul', 'small mb-2');

            for (const discarded of result.discarded) {
                list.append(element('li', null, `${discarded.fieldPath ?? '(campo non indicato)'} = ${discarded.value ?? '—'}: ${discarded.reason}`));
            }

            target.append(element('div', 'small fw-semibold', 'Scartati (non sono diventati proposte):'), list);
        }

        if (result.summary) {
            target.append(element('div', 'small text-muted', 'Sintesi scritta dal modello (testo, non un fatto):'), element('p', 'small', result.summary));
        }

        warnings(target, result);
        target.append(element('div', 'small text-muted', modelLine(result)));
    }

    function renderAuthority(target, result) {
        target.append(element('div', result.dataAvailable ? 'alert alert-success mb-2' : 'alert alert-secondary mb-2', result.answer));

        if (!result.dataAvailable) {
            target.append(element('div', 'small mb-2', 'L\'agente dichiara di non aver trovato il dato nei sottoinsiemi che può leggere.'));
        }

        if (result.unsupportedValues.length > 0) {
            target.append(element('div', 'alert alert-danger py-2 small mb-2',
                'Valori citati che non compaiono nei dati letti — risposta da prendere con riserva: ' + result.unsupportedValues.join(', ')));
        }
        else if (result.quotedValues.length > 0) {
            target.append(element('div', 'small text-success mb-2', 'Valori citati, tutti riscontrati nei dati letti: ' + result.quotedValues.join(', ')));
        }

        const read = result.subsetsRead.map(subset => `${subset.subsetId} di ${subset.uil}`).join('; ');

        const when = result.controlMoment
            ? `il ${new Date(result.controlMoment).toLocaleString('it-IT', { dateStyle: 'short', timeStyle: 'short' })} (controllo simulato)`
            : 'adesso';

        target.append(element('div', 'small text-muted', `Spedizioni a bordo ${when}: ${result.uils.length}. Sottoinsiemi letti: ${read || 'nessuno'}.`));
        warnings(target, result);
        target.append(element('div', 'small text-muted', modelLine(result)));
    }

    function render(target, mode, result) {
        target.replaceChildren();

        if (mode === 'reload') {
            renderMapping(target, result);
        }
        else {
            renderAuthority(target, result);
        }

        target.hidden = false;
    }

    function stepLine(step) {
        const failed = step.outcome && step.outcome !== 'ok';
        const line = element('li', failed ? 'text-danger' : null);

        // Un tool riuscito non ha messaggio: basta il nome. Se fallisce, l'esito dice perché (error:<codice>).
        const text = step.kind === 'tool'
            ? [step.tool, failed ? step.outcome : null, step.message].filter(Boolean).join(' — ')
            : step.message;

        line.append(element('span', 'text-muted me-1', step.kind === 'tool' ? '🔧' : '…'));
        line.append(document.createTextNode(text));

        if (step.elapsedMs !== null && step.elapsedMs !== undefined) {
            line.append(element('span', 'text-muted ms-1', `(${seconds(step.elapsedMs)})`));
        }

        return line;
    }

    function problemText(problem, status) {
        if (problem && (problem.detail || problem.title)) {
            return [problem.title, problem.detail].filter(Boolean).join(': ');
        }

        return `L'agente non ha risposto (HTTP ${status}).`;
    }

    // Legge lo stream SSE: gli eventi sono separati da una riga vuota, ciascuno con "event:" e una o più righe "data:".
    async function readEvents(response, onEvent) {
        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';

        for (;;) {
            const { done, value } = await reader.read();

            buffer += decoder.decode(value ?? new Uint8Array(), { stream: !done }).replace(/\r\n/g, '\n');

            let end;

            while ((end = buffer.indexOf('\n\n')) >= 0) {
                const block = buffer.slice(0, end);

                buffer = buffer.slice(end + 2);

                let name = 'message';
                const data = [];

                for (const line of block.split('\n')) {
                    if (line.startsWith('event:')) {
                        name = line.slice('event:'.length).trim();
                    }
                    else if (line.startsWith('data:')) {
                        data.push(line.slice('data:'.length).replace(/^ /, ''));
                    }
                }

                if (data.length > 0) {
                    onEvent(name, JSON.parse(data.join('\n')));
                }
            }

            if (done) {
                return;
            }
        }
    }

    async function run(form) {
        const box = form.closest('.card-body') ?? form.parentElement;
        const steps = box.querySelector('[data-agent-steps]');
        const error = box.querySelector('[data-agent-error]');
        const result = box.querySelector('[data-agent-result]');
        const time = box.querySelector('[data-agent-time]');
        const button = form.querySelector('button[type=submit]');
        const mode = form.dataset.agentDone;
        const request = {};
        let token = '';

        for (const [name, value] of new FormData(form)) {
            if (name === '__RequestVerificationToken') {
                token = value;
            }
            else {
                request[name] = value;
            }
        }

        steps.replaceChildren();
        error.hidden = true;
        result.hidden = true;
        button.disabled = true;

        const started = performance.now();
        const timer = setInterval(() => time.textContent = `in corso da ${seconds(performance.now() - started)}`, 200);
        let outcome = null;
        let failure = null;

        try {
            const response = await fetch(form.dataset.agentStream, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
                body: JSON.stringify(request)
            });

            if (!response.ok) {
                failure = problemText(await response.json().catch(() => null), response.status);
            }
            else {
                await readEvents(response, (name, data) => {
                    if (name === 'step') {
                        steps.append(stepLine(data));
                    }
                    else if (name === 'result') {
                        outcome = data;
                    }
                    else if (name === 'error') {
                        failure = problemText(data, response.status);
                    }
                });

                if (!outcome && !failure) {
                    failure = 'Lo stream si è chiuso senza un esito.';
                }
            }
        }
        catch (exception) {
            failure = `Servizio non raggiungibile: ${exception.message}`;
        }

        clearInterval(timer);
        time.textContent = `${failure ? 'interrotto' : 'concluso'} dopo ${seconds(performance.now() - started)}`;
        button.disabled = false;

        if (failure) {
            error.textContent = failure;
            error.hidden = false;

            return;
        }

        if (mode === 'reload') {
            // Le proposte le disegna la pagina: si ricarica, e l'esito del run si ritrova sopra l'elenco.
            try {
                sessionStorage.setItem(storageKey, JSON.stringify(outcome));
            }
            catch {
                // Senza sessionStorage la pagina si ricarica lo stesso: le proposte ci sono, manca solo il riepilogo.
            }

            location.reload();

            return;
        }

        render(result, mode, outcome);
    }

    for (const form of document.querySelectorAll('form[data-agent-stream]')) {
        form.addEventListener('submit', event => {
            event.preventDefault();
            run(form);
        });
    }

    // Dopo il ricaricamento che segue un run di mappatura: l'esito salvato si mostra una volta sola.
    let saved = null;

    try {
        saved = sessionStorage.getItem(storageKey);
        sessionStorage.removeItem(storageKey);
    }
    catch {
        saved = null;
    }

    if (saved) {
        const target = document.querySelector('[data-agent-saved-result]');

        if (target) {
            render(target, 'reload', JSON.parse(saved));
        }
    }
})();
