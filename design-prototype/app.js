const viewMap = {
  overview: document.querySelector('#view-overview'),
  accounts: document.querySelector('#view-accounts'),
  knowledge: document.querySelector('#view-knowledge')
};

const navButtons = [...document.querySelectorAll('[data-view]')];
const placeholder = document.querySelector('#view-placeholder');
const search = document.querySelector('#global-search');
const serviceButton = document.querySelector('#service-button');
const serviceLabel = serviceButton.querySelector('.button-label');
const serviceIcon = serviceButton.querySelector('.pause-icon');
const agentCard = document.querySelector('.agent-card');
const agentStateLabel = document.querySelector('.agent-state-label');
const agentAccountCount = document.querySelector('.agent-account-count');
const agentStateHelp = document.querySelector('.agent-state-help');
const toast = document.querySelector('.toast');
let toastTimer;
let serviceRunning = true;

const placeholders = {
  overview: '搜索账号、运行记录或设置…',
  accounts: '搜索账号名称或平台标签…',
  knowledge: '搜索知识、商品或规则…'
};

function showToast(message) {
  toast.textContent = message;
  toast.hidden = false;
  window.clearTimeout(toastTimer);
  toastTimer = window.setTimeout(() => { toast.hidden = true; }, 2200);
}

function switchView(target) {
  const next = viewMap[target] || placeholder;
  Object.values(viewMap).forEach(view => { view.hidden = view !== next; });
  placeholder.hidden = next !== placeholder;

  document.querySelectorAll('.nav-item').forEach(button => {
    const active = button.dataset.view === target;
    button.classList.toggle('is-active', active);
    if (active) button.setAttribute('aria-current', 'page');
    else button.removeAttribute('aria-current');
  });

  search.placeholder = placeholders[target] || '搜索当前管理模块…';
  window.location.hash = target;
}

function renderServiceState() {
  serviceLabel.textContent = serviceRunning ? '停止智能客服' : '启动智能客服';
  serviceIcon.classList.toggle('is-play', !serviceRunning);
  serviceIcon.textContent = serviceRunning ? '' : '▶';
  agentCard.classList.toggle('is-stopped', !serviceRunning);
  agentStateLabel.innerHTML = `<span class="status-dot"></span> ${serviceRunning ? '智能客服运行中' : '智能客服已停止'}`;
  agentAccountCount.textContent = serviceRunning ? '3 个账号已启用' : '0 个账号运行';
  agentStateHelp.textContent = serviceRunning ? '所有对话在原客服平台处理' : '不会检测或操作客服平台';
}

navButtons.forEach(button => {
  button.addEventListener('click', () => switchView(button.dataset.view));
});

document.querySelectorAll('[data-open-accounts]').forEach(button => {
  button.addEventListener('click', () => switchView('accounts'));
});

document.querySelectorAll('.filter-chip').forEach(button => {
  button.addEventListener('click', () => {
    const group = button.parentElement;
    group.querySelectorAll('.filter-chip').forEach(item => item.classList.remove('is-selected'));
    button.classList.add('is-selected');
  });
});

document.querySelectorAll('.managed-account-item').forEach(button => {
  button.addEventListener('click', () => {
    document.querySelectorAll('.managed-account-item').forEach(item => item.classList.remove('is-active'));
    button.classList.add('is-active');
  });
});

serviceButton.addEventListener('click', () => {
  serviceRunning = !serviceRunning;
  renderServiceState();
  showToast(serviceRunning
    ? '智能客服已启动，开始监测 3 个平台账号'
    : '智能客服已停止，所有自动化动作已安全取消');
});

document.querySelector('#sync-button').addEventListener('click', () => {
  showToast('知识库已同步，118 条已审核知识可用');
});

document.querySelectorAll('.setting-switch').forEach(button => {
  button.addEventListener('click', () => {
    const enabled = button.getAttribute('aria-checked') !== 'true';
    button.setAttribute('aria-checked', String(enabled));
    button.classList.toggle('is-on', enabled);
    showToast(`${button.getAttribute('aria-label')}已${enabled ? '开启' : '关闭'}`);
  });
});

document.querySelectorAll('[data-account-toggle]').forEach(button => {
  button.addEventListener('click', () => {
    const pausing = button.textContent.trim() === '暂停此账号';
    const badge = button.parentElement.querySelector('.status-badge');
    button.textContent = pausing ? '启用此账号' : '暂停此账号';
    button.classList.toggle('button-danger-soft', !pausing);
    button.classList.toggle('button-primary', pausing);
    badge.className = `status-badge ${pausing ? 'neutral' : 'success'}`;
    badge.innerHTML = `<i></i>${pausing ? '账号已暂停' : '账号运行中'}`;
    showToast(pausing ? '此账号已暂停，不再进入处理队列' : '此账号已启用，开始监测平台消息');
  });
});

document.querySelectorAll('.account-detail-panel .row-button, .add-account-button, .editor-actions .button').forEach(button => {
  button.addEventListener('click', () => showToast(`${button.textContent.trim()}操作已记录`));
});

const initialView = window.location.hash.replace('#', '');
switchView(viewMap[initialView] ? initialView : 'overview');
renderServiceState();
