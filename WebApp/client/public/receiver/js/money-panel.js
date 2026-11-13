export class MoneyPanel {
  constructor(root) {
    this.root = root;
    this.balance = root.querySelector('[data-money-balance]');
    this.formatter = new Intl.NumberFormat();
    this.reset();
  }

  setState(state) {
    if (state?.type !== 'money-state' || !Number.isSafeInteger(state.balance) || state.balance < 0) return;
    this.balance.textContent = this.formatter.format(state.balance);
    this.root.hidden = false;
  }

  reset() {
    this.balance.textContent = '—';
    this.root.hidden = true;
  }
}
