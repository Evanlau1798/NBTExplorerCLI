const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
document.documentElement.dataset.enhanced = 'true';

// 1. Sticky translucent header border on scroll
const header = document.querySelector<HTMLElement>('[data-header]');
if (header) {
  const updateHeader = () => {
    header.dataset.scrolled = window.scrollY > 10 ? 'true' : 'false';
  };
  updateHeader();
  window.addEventListener('scroll', updateHeader, { passive: true });
}

// 2. Dark / Light theme toggle synced with Starlight (localStorage 'starlight-theme')
const themeToggle = document.getElementById('landing-theme-toggle') as HTMLButtonElement | null;
if (themeToggle) {
  const syncThemeAria = (theme: 'dark' | 'light') => {
    const label = theme === 'light' ? themeToggle.dataset.labelDark : themeToggle.dataset.labelLight;
    if (label) {
      themeToggle.setAttribute('aria-label', label);
      themeToggle.setAttribute('title', label);
    }
    const meta = document.querySelector<HTMLMetaElement>('meta[name="theme-color"]');
    if (meta) meta.setAttribute('content', theme === 'light' ? '#ffffff' : '#09090b');
  };

  const currentTheme = document.documentElement.dataset.theme === 'light' ? 'light' : 'dark';
  syncThemeAria(currentTheme);

  themeToggle.addEventListener('click', () => {
    const next: 'dark' | 'light' = document.documentElement.dataset.theme === 'light' ? 'dark' : 'light';
    document.documentElement.dataset.theme = next;
    try {
      localStorage.setItem('starlight-theme', next);
    } catch {}
    syncThemeAria(next);
  });
}

// 3. Mobile navigation drawer
const menuToggle = document.querySelector<HTMLButtonElement>('.menu-toggle');
const navigation = document.querySelector<HTMLElement>('.site-nav');
const mobile = window.matchMedia('(max-width: 760px)');
if (menuToggle && navigation) {
  const menuLabel = menuToggle.querySelector<HTMLElement>('.menu-label');
  const setMenu = (open: boolean) => {
    menuToggle.setAttribute('aria-expanded', String(open));
    navigation.dataset.open = String(open);
    if (menuLabel) {
      menuLabel.textContent = (open ? menuToggle.dataset.closeLabel : menuToggle.dataset.openLabel) ?? '';
    }
  };
  menuToggle.addEventListener('click', () => setMenu(menuToggle.getAttribute('aria-expanded') !== 'true'));
  document.addEventListener('keydown', (event) => {
    if (event.key === 'Escape' && mobile.matches && navigation.dataset.open === 'true') {
      setMenu(false);
      menuToggle.focus();
    }
  });
  document.addEventListener('click', (event) => {
    if (
      event.target instanceof Node &&
      mobile.matches &&
      !navigation.contains(event.target) &&
      !menuToggle.contains(event.target)
    ) {
      setMenu(false);
    }
  });
  mobile.addEventListener('change', () => setMenu(false));
  setMenu(false);
  menuToggle.hidden = false;
}

// 4. Hero Stage scene switcher + sliding indicator + synced CLI quick command
const stageTabs = [...document.querySelectorAll<HTMLButtonElement>('.stage-tab')];
const stagePanels = [...document.querySelectorAll<HTMLElement>('[data-scene-panel]')];
const stagePill = document.querySelector<HTMLElement>('.stage-tab-pill');
const heroToggleBtn = document.getElementById('hero-translate-toggle') as HTMLButtonElement | null;
const translatePanel = document.getElementById('stage-panel-translate');
const cliCodeEl = document.getElementById('hero-cli-code');
const cliCopyBtn = document.getElementById('hero-cli-copy') as HTMLButtonElement | null;

const movePill = (activeBtn: HTMLButtonElement) => {
  if (!stagePill) return;
  stagePill.style.width = `${activeBtn.offsetWidth}px`;
  stagePill.style.transform = `translateX(${activeBtn.offsetLeft - 3}px)`;
};

const selectScene = (index: number) => {
  stageTabs.forEach((tab, i) => {
    const active = i === index;
    tab.setAttribute('aria-selected', String(active));
    tab.tabIndex = active ? 0 : -1;
    const panel = stagePanels[i];
    if (panel) {
      panel.hidden = !active;
      if (active) {
        panel.classList.remove('is-entering');
        void panel.offsetWidth;
        panel.classList.add('is-entering');
      }
    }
  });
  const currentTab = stageTabs[index];
  if (currentTab) {
    movePill(currentTab);
    if (heroToggleBtn) {
      heroToggleBtn.hidden = currentTab.dataset.scene !== 'translate';
    }
    if (cliCodeEl && currentTab.dataset.cliCmd) {
      cliCodeEl.textContent = currentTab.dataset.cliCmd;
    }
  }
};

if (stageTabs.length) {
  stageTabs.forEach((tab, index) => {
    tab.addEventListener('click', () => selectScene(index));
    tab.addEventListener('keydown', (event) => {
      let next: number;
      switch (event.key) {
        case 'ArrowRight': next = (index + 1) % stageTabs.length; break;
        case 'ArrowLeft': next = (index + stageTabs.length - 1) % stageTabs.length; break;
        case 'Home': next = 0; break;
        case 'End': next = stageTabs.length - 1; break;
        default: return;
      }
      event.preventDefault();
      selectScene(next);
      stageTabs[next].focus();
    });
  });
  requestAnimationFrame(() => selectScene(0));
  window.addEventListener('resize', () => {
    const activeIdx = stageTabs.findIndex((t) => t.getAttribute('aria-selected') === 'true');
    if (activeIdx >= 0) movePill(stageTabs[activeIdx]);
  });
}

// 5. Copyable Hero CLI command button
if (cliCopyBtn && cliCodeEl) {
  const copyTextEl = cliCopyBtn.querySelector<HTMLElement>('.cli-copy-text');
  let resetTimer = 0;
  cliCopyBtn.addEventListener('click', async () => {
    const cmd = cliCodeEl.textContent?.trim() ?? '';
    if (!cmd) return;
    try {
      await navigator.clipboard.writeText(cmd);
    } catch {
      const helper = document.createElement('textarea');
      helper.value = cmd;
      helper.style.position = 'fixed';
      helper.style.opacity = '0';
      document.body.appendChild(helper);
      helper.select();
      document.execCommand('copy');
      helper.remove();
    }
    cliCopyBtn.dataset.copied = 'true';
    const copiedLabel = cliCopyBtn.dataset.copiedLabel ?? 'Copied';
    cliCopyBtn.setAttribute('aria-label', copiedLabel);
    if (copyTextEl) copyTextEl.textContent = copiedLabel;
    window.clearTimeout(resetTimer);
    resetTimer = window.setTimeout(() => {
      cliCopyBtn.dataset.copied = 'false';
      const defaultLabel = cliCopyBtn.dataset.copyLabel ?? 'Copy command';
      cliCopyBtn.setAttribute('aria-label', defaultLabel);
      if (copyTextEl) copyTextEl.textContent = defaultLabel;
    }, 1800);
  });
}

// 6. Hero live Original <-> Translated toggle (defaults to crisp translated state)
if (heroToggleBtn && translatePanel) {
  const flipText = heroToggleBtn.querySelector<HTMLElement>('.flip-text');
  const setHeroLang = (mode: 'original' | 'translated') => {
    heroToggleBtn.dataset.mode = mode;
    heroToggleBtn.setAttribute('aria-pressed', String(mode === 'translated'));
    translatePanel.dataset.langState = mode;
    if (flipText) {
      flipText.textContent =
        mode === 'translated'
          ? (heroToggleBtn.dataset.labelTranslated ?? '')
          : (heroToggleBtn.dataset.labelOriginal ?? '');
    }
  };
  heroToggleBtn.addEventListener('click', () => {
    const next = heroToggleBtn.dataset.mode === 'translated' ? 'original' : 'translated';
    setHeroLang(next);
  });
  setHeroLang('translated');
}

// 7. Bento Cell 1: Live Original / Translated switch
const bentoTranslateVisual = document.querySelector<HTMLElement>('[data-bento-lang]');
if (bentoTranslateVisual) {
  const buttons = [...bentoTranslateVisual.querySelectorAll<HTMLButtonElement>('[data-set-bento-lang]')];
  buttons.forEach((btn) => {
    btn.addEventListener('click', () => {
      const langVal = btn.dataset.setBentoLang ?? 'zh';
      bentoTranslateVisual.dataset.bentoLang = langVal;
      buttons.forEach((b) => {
        const active = b === btn;
        b.classList.toggle('is-active', active);
        b.setAttribute('aria-pressed', String(active));
      });
    });
  });
}

// 8. Bento Cell 2: Interactive NBT Tree expand/collapse
document.querySelectorAll<HTMLElement>('[data-tree-branch]').forEach((branch) => {
  const btn = branch.querySelector<HTMLButtonElement>('.nbt-branch-toggle');
  if (!btn) return;
  btn.addEventListener('click', () => {
    const nextOpen = branch.dataset.open !== 'true';
    branch.dataset.open = String(nextOpen);
    btn.setAttribute('aria-expanded', String(nextOpen));
  });
});

// 9. Bento Cell 3: Direct Map / Resource Pack .zip toggle
const bentoPackVisual = document.querySelector<HTMLElement>('[data-pack-mode]');
if (bentoPackVisual) {
  const buttons = [...bentoPackVisual.querySelectorAll<HTMLButtonElement>('[data-set-pack-mode]')];
  buttons.forEach((btn) => {
    btn.addEventListener('click', () => {
      const mode = btn.dataset.setPackMode ?? 'zip';
      bentoPackVisual.dataset.packMode = mode;
      buttons.forEach((b) => {
        const active = b === btn;
        b.classList.toggle('is-active', active);
        b.setAttribute('aria-pressed', String(active));
      });
    });
  });
}

// 10. Bento Cell 4: State-driven Safety Timeline checkpoints
const safetyVisual = document.querySelector<HTMLElement>('[data-active-step]');
if (safetyVisual) {
  const nodes = [...safetyVisual.querySelectorAll<HTMLButtonElement>('[data-safety-step]')];
  const details = [...safetyVisual.querySelectorAll<HTMLElement>('[data-safety-detail]')];
  const selectStep = (idx: number) => {
    safetyVisual.dataset.activeStep = String(idx);
    nodes.forEach((n, i) => {
      const active = i === idx;
      const reached = i <= idx;
      n.classList.toggle('is-active', active);
      n.classList.toggle('is-reached', reached);
      n.setAttribute('aria-pressed', String(active));
    });
    details.forEach((d, i) => d.classList.toggle('is-active', i === idx));
  };
  nodes.forEach((node, idx) => node.addEventListener('click', () => selectStep(idx)));
  selectStep(0);
}

// 11. Precision 1px border & hairline spotlight tracking (zero 3D tilt)
const finePointer = window.matchMedia('(pointer: fine)').matches;
if (finePointer && !prefersReducedMotion) {
  document.querySelectorAll<HTMLElement>('[data-spotlight]').forEach((el) => {
    const updatePointer = (event: PointerEvent) => {
      const rect = el.getBoundingClientRect();
      el.style.setProperty('--mx', `${(event.clientX - rect.left).toFixed(1)}px`);
      el.style.setProperty('--my', `${(event.clientY - rect.top).toFixed(1)}px`);
    };
    el.addEventListener('pointerenter', updatePointer);
    el.addEventListener('pointermove', updatePointer);
  });
}

// 12. Crisp scroll-triggered reveal
const revealNodes = [...document.querySelectorAll<HTMLElement>('[data-reveal]')];
if (!prefersReducedMotion && 'IntersectionObserver' in window && revealNodes.length) {
  const markVisible = (node: HTMLElement) => {
    node.classList.add('is-visible');
  };
  const revealPassed = () => {
    const thresholdY = window.innerHeight + 60;
    revealNodes.forEach((node) => {
      if (!node.classList.contains('is-visible') && node.getBoundingClientRect().top <= thresholdY) {
        markVisible(node);
      }
    });
  };
  const io = new IntersectionObserver(
    (entries) => {
      entries.forEach((entry) => {
        if (entry.isIntersecting) {
          markVisible(entry.target as HTMLElement);
          io.unobserve(entry.target);
        }
      });
      revealPassed();
    },
    { threshold: 0.06, rootMargin: '0px 0px -16px 0px' }
  );
  revealNodes.forEach((node, idx) => {
    node.style.setProperty('--reveal-delay', `${Math.min(idx * 45, 160)}ms`);
    io.observe(node);
  });
  let scrollScheduled = false;
  window.addEventListener(
    'scroll',
    () => {
      if (scrollScheduled) return;
      scrollScheduled = true;
      requestAnimationFrame(() => {
        scrollScheduled = false;
        revealPassed();
      });
    },
    { passive: true }
  );
  revealPassed();
} else {
  revealNodes.forEach((node) => node.classList.add('is-visible'));
}
