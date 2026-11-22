export async function copyCommand(text: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    const helper = document.createElement('textarea');
    helper.value = text;
    helper.style.position = 'fixed';
    helper.style.opacity = '0';
    document.body.appendChild(helper);
    try {
      helper.select();
      return document.execCommand('copy');
    } catch {
      return false;
    } finally {
      helper.remove();
    }
  }
}
