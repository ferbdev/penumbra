(() => {
  const DOWNLOAD_URL = 'https://github.com/ferbdev/penumbra/releases/latest/download/Penumbra.exe';
  // Quando o Penumbra.exe estiver assinado, o passo do SmartScreen deixa de ser necessário.
  const SIGNED = false;

  const messages = {
    'pt-BR': {
      guideTitle: 'Falta só abrir o arquivo',
      browserStep: {
        edge: 'Na janela de downloads do Edge, aqui em cima, clique em <strong>Abrir arquivo</strong>.',
        chrome: 'Clique no ícone de downloads do Chrome, aqui em cima, e depois em <strong>Penumbra.exe</strong>.',
        firefox: 'Clique na seta de downloads do Firefox, aqui em cima, e depois em <strong>Penumbra.exe</strong>.',
        other: 'Abra o <strong>Penumbra.exe</strong> na lista de downloads do navegador.',
      },
      shortcut: 'Não achou? <kbd>Ctrl</kbd> + <kbd>J</kbd> abre os downloads.',
      smartscreenStep: 'Se o Windows mostrar o aviso azul, clique em <strong>Mais informações</strong> e depois em <strong>Executar assim mesmo</strong>.',
      smartscreenTitle: 'O Windows protegeu o computador',
      smartscreenMore: 'Mais informações',
      smartscreenRun: 'Executar assim mesmo',
      trayStep: 'O widget abre sozinho na primeira vez. Depois, ele fica no ícone da bandeja do Windows.',
      done: 'Já abri, obrigado',
      downloadAgain: 'Baixar de novo',
      notWindows: 'O Penumbra é para Windows 11. Abra esta página no seu PC para baixar.',
      close: 'Fechar',
    },
    en: {
      guideTitle: 'Just open the file',
      browserStep: {
        edge: 'In Edge’s downloads panel up here, click <strong>Open file</strong>.',
        chrome: 'Click Chrome’s downloads icon up here, then <strong>Penumbra.exe</strong>.',
        firefox: 'Click Firefox’s downloads arrow up here, then <strong>Penumbra.exe</strong>.',
        other: 'Open <strong>Penumbra.exe</strong> from your browser’s downloads list.',
      },
      shortcut: 'Can’t find it? <kbd>Ctrl</kbd> + <kbd>J</kbd> opens your downloads.',
      smartscreenStep: 'If Windows shows the blue warning, click <strong>More info</strong>, then <strong>Run anyway</strong>.',
      smartscreenTitle: 'Windows protected your PC',
      smartscreenMore: 'More info',
      smartscreenRun: 'Run anyway',
      trayStep: 'The widget opens by itself the first time. After that, it lives in the Windows tray icon.',
      done: 'Done, thanks',
      downloadAgain: 'Download again',
      notWindows: 'Penumbra is for Windows 11. Open this page on your PC to download it.',
      close: 'Close',
    },
  };

  const lang = document.documentElement.lang === 'en' ? 'en' : 'pt-BR';
  const t = messages[lang];
  const isWindows = /Windows/i.test(navigator.userAgent);
  const browser = detectBrowser(navigator.userAgent);

  function detectBrowser(ua) {
    if (/Edg\//.test(ua)) return 'edge';
    if (/Firefox\//.test(ua)) return 'firefox';
    if (/Chrome\//.test(ua)) return 'chrome';
    return 'other';
  }

  // Guia pós-download: seta para a área de downloads do navegador + passos.
  const guide = document.createElement('div');
  guide.className = 'download-guide';
  guide.setAttribute('role', 'dialog');
  guide.setAttribute('aria-modal', 'false');
  guide.setAttribute('aria-labelledby', 'download-guide-title');
  document.body.appendChild(guide);

  function showGuide() {
    const smartscreen = SIGNED ? '' : `
      <li>
        ${t.smartscreenStep}
        <div class="smartscreen-mock" aria-hidden="true">
          <span class="ss-title">${t.smartscreenTitle}</span>
          <span class="ss-more">${t.smartscreenMore}</span>
          <span class="ss-run">${t.smartscreenRun}</span>
        </div>
      </li>`;

    guide.innerHTML = `
      <div class="guide-arrow" aria-hidden="true">
        <svg viewBox="0 0 48 48" fill="none" stroke="currentColor" stroke-width="4" stroke-linecap="round" stroke-linejoin="round"><path d="M12 36 36 12M18 12h18v18"/></svg>
      </div>
      <div class="guide-panel glass">
        <button type="button" class="guide-close" aria-label="${t.close}">×</button>
        <h4 id="download-guide-title">${t.guideTitle}</h4>
        <ol class="guide-steps">
          <li>${t.browserStep[browser]}<span class="guide-hint">${t.shortcut}</span></li>
          ${smartscreen}
          <li>${t.trayStep}</li>
        </ol>
        <div class="guide-actions">
          <button type="button" class="btn btn-primary" data-guide="done">${t.done}</button>
          <a class="btn btn-ghost" href="${DOWNLOAD_URL}" data-guide="again">${t.downloadAgain}</a>
        </div>
      </div>`;

    guide.querySelector('.guide-close').addEventListener('click', hideGuide);
    guide.querySelector('[data-guide="done"]').addEventListener('click', hideGuide);
    guide.querySelector('[data-guide="again"]').addEventListener('click', (event) => {
      event.preventDefault();
      startDownload();
    });
    guide.classList.add('show');
    guide.querySelector('[data-guide="done"]').focus({ preventScroll: true });
  }

  function hideGuide() {
    guide.classList.remove('show');
  }

  document.addEventListener('keydown', (event) => {
    if (event.key === 'Escape') hideGuide();
  });

  function startDownload() {
    const link = document.createElement('a');
    link.href = DOWNLOAD_URL;
    link.rel = 'noopener';
    document.body.appendChild(link);
    link.click();
    link.remove();
    showGuide();
  }

  document.querySelectorAll('[data-action="download"]').forEach((link) => {
    link.href = DOWNLOAD_URL;
    if (!isWindows) return;
    link.addEventListener('click', (event) => {
      event.preventDefault();
      startDownload();
    });
  });

  if (!isWindows) {
    document.querySelectorAll('[data-platform-note]').forEach((el) => {
      el.textContent = t.notWindows;
    });
  }
})();
