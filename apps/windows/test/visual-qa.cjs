const { app, BrowserWindow } = require("electron");
const { writeFile } = require("node:fs/promises");
const { join, resolve } = require("node:path");

async function capture() {
  const window = new BrowserWindow({
    width: 1180,
    height: 800,
    show: false,
    backgroundColor: "#080d14",
    webPreferences: {
      preload: join(__dirname, "visual-qa-preload.cjs"),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: false,
    },
  });
  await window.loadFile(
    join(__dirname, "..", "dist", "renderer", "index.html"),
    { query: { view: "dashboard" } },
  );
  await new Promise((resolvePromise) => setTimeout(resolvePromise, 400));
  const scrollTop = Number(process.env.USAGEAPP_QA_SCROLL ?? 0);
  if (Number.isFinite(scrollTop) && scrollTop > 0) {
    await window.webContents.executeJavaScript(
      `document.querySelector(".dashboard-shell").scrollTop = ${Math.round(
        scrollTop,
      )}`,
    );
    await new Promise((resolvePromise) => setTimeout(resolvePromise, 150));
  }
  const image = await window.webContents.capturePage();
  const outputPath = resolve(
    process.env.USAGEAPP_QA_OUTPUT ?? "usageapp-dashboard-qa.png",
  );
  await writeFile(outputPath, image.toPNG());
  console.log(outputPath);
  window.destroy();
}

app.whenReady().then(capture).then(() => app.quit()).catch((error) => {
  console.error(error);
  app.exit(1);
});
