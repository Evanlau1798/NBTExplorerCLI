const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
document.documentElement.dataset.enhanced = 'true';

// 1. Sticky glass header elevation on scroll
const header = document.querySelector<HTMLElement>('[data-header]');
if (header) {
  const updateHeader = () => {
    header.dataset.scrolled = window.scrollY > 12 ? 'true' : 'false';
  };
  updateHeader();
  window.addEventListener('scroll', updateHeader, { passive: true });
}

// 2. Mobile navigation drawer
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

// 3. Hero Stage scene switcher + animated sliding pill
const stageTabs = [...document.querySelectorAll<HTMLButtonElement>('.stage-tab')];
const stagePanels = [...document.querySelectorAll<HTMLElement>('[data-scene-panel]')];
const stagePill = document.querySelector<HTMLElement>('.stage-tab-pill');
const heroToggleBtn = document.getElementById('hero-translate-toggle') as HTMLButtonElement | null;
const translatePanel = document.getElementById('stage-panel-translate');

const movePill = (activeBtn: HTMLButtonElement) => {
  if (!stagePill) return;
  stagePill.style.width = `${activeBtn.offsetWidth}px`;
  stagePill.style.transform = `translateX(${activeBtn.offsetLeft - 4}px)`;
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

// 4. Hero live EN <-> ZH morph toggle
if (heroToggleBtn && translatePanel) {
  const flipText = heroToggleBtn.querySelector<HTMLElement>('.flip-text');
  let userToggledHero = false;
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
    userToggledHero = true;
    const next = heroToggleBtn.dataset.mode === 'translated' ? 'original' : 'translated';
    setHeroLang(next);
  });
  if (!prefersReducedMotion) {
    setHeroLang('original');
    window.setTimeout(() => {
      if (!userToggledHero) setHeroLang('translated');
    }, 1500);
  }
}

// 5. Bento Card 1: Live EN / ZH switch
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

// 6. Bento Card 2: Interactive NBT Tree expand/collapse
document.querySelectorAll<HTMLElement>('[data-tree-branch]').forEach((branch) => {
  const btn = branch.querySelector<HTMLButtonElement>('.nbt-branch-toggle');
  if (!btn) return;
  btn.addEventListener('click', () => {
    const nextOpen = branch.dataset.open !== 'true';
    branch.dataset.open = String(nextOpen);
    btn.setAttribute('aria-expanded', String(nextOpen));
  });
});

// 7. Bento Card 3: Direct Map / Resource Pack .zip toggle
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

// 8. Bento Card 4: Safety timeline checkpoints
const safetyVisual = document.querySelector<HTMLElement>('[data-active-step]');
if (safetyVisual) {
  const nodes = [...safetyVisual.querySelectorAll<HTMLButtonElement>('[data-safety-step]')];
  const details = [...safetyVisual.querySelectorAll<HTMLElement>('[data-safety-detail]')];
  const selectStep = (idx: number) => {
    safetyVisual.dataset.activeStep = String(idx);
    nodes.forEach((n, i) => {
      const active = i === idx;
      n.classList.toggle('is-active', active);
      n.setAttribute('aria-pressed', String(active));
    });
    details.forEach((d, i) => d.classList.toggle('is-active', i === idx));
  };
  nodes.forEach((node, idx) => node.addEventListener('click', () => selectStep(idx)));
}

// 9. Cursor spotlight + responsive rAF-interpolated 3D tilt (zero transition-delay lag)
const finePointer = window.matchMedia('(pointer: fine)').matches;
if (finePointer && !prefersReducedMotion) {
  const clamp = (v: number, min: number, max: number) => Math.max(min, Math.min(max, v));
  document.querySelectorAll<HTMLElement>('[data-spotlight]').forEach((el) => {
    const hasTilt = el.hasAttribute('data-tilt');
    let targetRx = 0;
    let targetRy = 0;
    let targetScale = 1;
    let currentRx = 0;
    let currentRy = 0;
    let currentScale = 1;
    let hovering = false;
    let rafId = 0;

    const tickTilt = () => {
      const ease = hovering ? 0.3 : 0.16;
      currentRx += (targetRx - currentRx) * ease;
      currentRy += (targetRy - currentRy) * ease;
      currentScale += (targetScale - currentScale) * ease;

      el.style.setProperty('--rx', `${currentRx.toFixed(3)}deg`);
      el.style.setProperty('--ry', `${currentRy.toFixed(3)}deg`);
      el.style.setProperty('--card-scale', currentScale.toFixed(4));

      const settled =
        Math.abs(targetRx - currentRx) < 0.015 &&
        Math.abs(targetRy - currentRy) < 0.015 &&
        Math.abs(targetScale - currentScale) < 0.0005;

      if (settled) {
        currentRx = targetRx;
        currentRy = targetRy;
        currentScale = targetScale;
        el.style.setProperty('--rx', `${currentRx.toFixed(2)}deg`);
        el.style.setProperty('--ry', `${currentRy.toFixed(2)}deg`);
        el.style.setProperty('--card-scale', currentScale.toFixed(3));
        rafId = 0;
        if (!hovering) el.classList.remove('is-tilting');
        return;
      }
      rafId = requestAnimationFrame(tickTilt);
    };

    const startTiltLoop = () => {
      if (!rafId) rafId = requestAnimationFrame(tickTilt);
    };

    const updateFromEvent = (event: PointerEvent) => {
      const rect = el.getBoundingClientRect();
      const x = event.clientX - rect.left;
      const y = event.clientY - rect.top;
      el.style.setProperty('--mx', `${x.toFixed(1)}px`);
      el.style.setProperty('--my', `${y.toFixed(1)}px`);
      if (hasTilt && rect.width > 0 && rect.height > 0) {
        const nx = clamp(x / rect.width - 0.5, -0.5, 0.5);
        const ny = clamp(y / rect.height - 0.5, -0.5, 0.5);
        targetRx = -ny * 8.4;
        targetRy = nx * 8.4;
        startTiltLoop();
      }
    };

    el.addEventListener('pointerenter', (event) => {
      hovering = true;
      if (hasTilt) {
        el.classList.add('is-visible', 'is-revealed', 'is-tilting');
        targetScale = 1.008;
      }
      updateFromEvent(event);
    });

    el.addEventListener('pointermove', updateFromEvent);

    const resetTilt = () => {
      hovering = false;
      if (hasTilt) {
        targetRx = 0;
        targetRy = 0;
        targetScale = 1;
        startTiltLoop();
      }
    };

    el.addEventListener('pointerleave', resetTilt);
    el.addEventListener('pointercancel', resetTilt);
  });
}

// 10. Scroll-triggered stagger reveal (also reveals elements passed when jumping straight to bottom)
const revealNodes = [...document.querySelectorAll<HTMLElement>('[data-reveal]')];
if (!prefersReducedMotion && 'IntersectionObserver' in window && revealNodes.length) {
  const markVisible = (node: HTMLElement) => {
    if (node.classList.contains('is-visible')) return;
    node.classList.add('is-visible');
    const delayMs = parseFloat(node.style.getPropertyValue('--reveal-delay')) || 0;
    window.setTimeout(() => node.classList.add('is-revealed'), delayMs + 620);
  };
  const revealPassed = () => {
    const thresholdY = window.innerHeight + 80;
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
    { threshold: 0.08, rootMargin: '0px 0px -24px 0px' }
  );
  revealNodes.forEach((node, idx) => {
    node.style.setProperty('--reveal-delay', `${Math.min(idx * 60, 220)}ms`);
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
  revealNodes.forEach((node) => node.classList.add('is-visible', 'is-revealed'));
}

// 11. Lightweight floating digitized voxel pixels (Grass -> Cyan sparks)
const canvas = document.getElementById('voxel-canvas') as HTMLCanvasElement | null;
if (canvas && !prefersReducedMotion) {
  const ctx = canvas.getContext('2d');
  if (ctx) {
    type Voxel = { x: number; y: number; size: number; vy: number; alpha: number; color: string; phase: number };
    const colors = ['#6AA04C', '#4ADE80', '#38BDF8', '#7DD3FC'];
    let width = 0;
    let height = 0;
    let voxels: Voxel[] = [];
    let rafId = 0;
    let running = true;

    const resize = () => {
      const dpr = Math.min(window.devicePixelRatio || 1, 2);
      width = window.innerWidth;
      height = Math.min(window.innerHeight, 920);
      canvas.width = width * dpr;
      canvas.height = height * dpr;
      canvas.style.width = `${width}px`;
      canvas.style.height = `${height}px`;
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      const count = Math.max(14, Math.min(34, Math.floor(width / 46)));
      voxels = Array.from({ length: count }, () => ({
        x: Math.random() * width,
        y: Math.random() * height,
        size: Math.random() > 0.55 ? 5 : 3.5,
        vy: 0.18 + Math.random() * 0.32,
        alpha: 0.14 + Math.random() * 0.28,
        color: colors[Math.floor(Math.random() * colors.length)],
        phase: Math.random() * Math.PI * 2,
      }));
    };

    const draw = (time: number) => {
      if (!running) return;
      ctx.clearRect(0, 0, width, height);
      for (const v of voxels) {
        v.y -= v.vy;
        if (v.y < -16) {
          v.y = height + 16;
          v.x = Math.random() * width;
        }
        const twinkle = 0.55 + 0.45 * Math.sin(time * 0.0014 + v.phase);
        const edgeFade = Math.min(1, v.y / 140, (height - v.y) / 140);
        ctx.globalAlpha = Math.max(0, v.alpha * twinkle * edgeFade);
        ctx.fillStyle = v.color;
        ctx.fillRect(Math.round(v.x), Math.round(v.y), v.size, v.size);
      }
      rafId = requestAnimationFrame(draw);
    };

    resize();
    window.addEventListener('resize', resize, { passive: true });
    document.addEventListener('visibilitychange', () => {
      running = !document.hidden;
      if (running) rafId = requestAnimationFrame(draw);
      else cancelAnimationFrame(rafId);
    });
    rafId = requestAnimationFrame(draw);
  }
}
