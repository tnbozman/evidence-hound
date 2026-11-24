// Print an HTML file to PDF with the pre-installed Playwright: node pdf.js <in.html> <out.pdf>
const { chromium } = require('playwright');
(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage();
  await page.goto('file://' + require('path').resolve(process.argv[2]));
  await page.pdf({ path: process.argv[3], preferCSSPageSize: true, printBackground: true });
  await browser.close();
})();
