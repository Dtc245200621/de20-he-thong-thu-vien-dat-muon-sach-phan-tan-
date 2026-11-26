const modeTabs = document.querySelectorAll(".login-tab");
const identifier = document.getElementById("identifier");
const identifierLabel = document.getElementById("identifier-label");
let loginMode = "email";

async function postJson(path, data) {
  const response = await fetch(path, { method: "POST", credentials: "same-origin", headers: { "Content-Type": "application/json" }, body: JSON.stringify(data) });
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(payload.error || (response.status === 401 ? "Thông tin đăng nhập chưa chính xác." : "Không thể thực hiện yêu cầu."));
  return payload;
}

function setBusy(form, busy) {
  const button = form.querySelector("button[type=submit]");
  button.disabled = busy;
  button.setAttribute("aria-busy", String(busy));
}

modeTabs.forEach(tab => tab.addEventListener("click", () => {
  loginMode = tab.dataset.mode;
  modeTabs.forEach(item => { const active = item === tab; item.classList.toggle("active", active); item.setAttribute("aria-selected", String(active)); });
  if (loginMode === "phone") {
    identifier.type = "tel"; identifier.name = "phone"; identifier.placeholder = "Ví dụ: 090 123 4567"; identifier.autocomplete = "tel"; identifier.pattern = "[0-9+()\\s.-]{9,20}"; identifier.title = "Nhập số điện thoại từ 9 đến 20 ký tự số"; identifierLabel.textContent = "Số điện thoại";
  } else {
    identifier.type = "email"; identifier.name = "email"; identifier.placeholder = "tenban@email.com"; identifier.autocomplete = "username"; identifier.removeAttribute("pattern"); identifier.removeAttribute("title"); identifierLabel.textContent = "Địa chỉ email";
  }
  identifier.value = ""; document.getElementById("login-feedback").textContent = ""; identifier.focus();
}));

document.getElementById("password-toggle").addEventListener("click", event => {
  const password = document.getElementById("password"), button = event.currentTarget;
  const reveal = password.type === "password"; password.type = reveal ? "text" : "password"; button.textContent = reveal ? "Ẩn" : "Hiện"; button.setAttribute("aria-label", reveal ? "Ẩn mật khẩu" : "Hiện mật khẩu");
});

document.getElementById("forgot-link").addEventListener("click", () => {
  document.getElementById("sign-in-view").hidden = true; document.getElementById("reset-view").hidden = false;
  document.getElementById("reset-feedback").textContent = ""; document.getElementById("reset-email").value = loginMode === "email" ? identifier.value : ""; document.getElementById("reset-email").focus();
});
document.getElementById("back-to-login").addEventListener("click", () => {
  document.getElementById("reset-view").hidden = true; document.getElementById("sign-in-view").hidden = false; document.getElementById("login-feedback").textContent = ""; document.getElementById("identifier").focus();
});

document.getElementById("login-form").addEventListener("submit", async event => {
  event.preventDefault();
  const form = event.currentTarget, feedback = document.getElementById("login-feedback");
  feedback.className = "form-feedback"; feedback.textContent = "Đang kiểm tra thông tin…"; setBusy(form, true);
  try {
    const result = await postJson("/auth/login", { identifier: identifier.value.trim(), password: document.getElementById("password").value, rememberMe: document.getElementById("remember").checked });
    localStorage.setItem("library-user-id", result.user.id);
    window.location.assign("index.html");
  } catch (error) { feedback.textContent = error.message; }
  finally { setBusy(form, false); }
});

document.getElementById("reset-form").addEventListener("submit", async event => {
  event.preventDefault();
  const form = event.currentTarget, feedback = document.getElementById("reset-feedback");
  feedback.className = "form-feedback"; feedback.textContent = "Đang gửi yêu cầu…"; setBusy(form, true);
  try {
    const result = await postJson("/auth/forgot-password", { email: document.getElementById("reset-email").value.trim() });
    feedback.className = "form-feedback success"; feedback.textContent = result.message;
    if (result.developmentResetUrl) {
      const link = document.createElement("a"); link.href = result.developmentResetUrl; link.textContent = " Mở liên kết đặt lại (chế độ phát triển)"; link.className = "inline-link"; feedback.append(link);
    }
  } catch (error) { feedback.textContent = error.message; }
  finally { setBusy(form, false); }
});

document.getElementById("new-password-form").addEventListener("submit", async event => {
  event.preventDefault();
  const form = event.currentTarget, feedback = document.getElementById("new-password-feedback");
  feedback.className = "form-feedback"; feedback.textContent = "Đang cập nhật mật khẩu…"; setBusy(form, true);
  try {
    const result = await postJson("/auth/reset-password", { email: document.getElementById("reset-account-email").value.trim(), token: new URLSearchParams(location.search).get("resetToken"), newPassword: document.getElementById("new-password").value });
    feedback.className = "form-feedback success"; feedback.textContent = result.message;
    document.getElementById("reset-form").hidden = true;
    setTimeout(() => { location.assign("login.html"); }, 1400);
  } catch (error) { feedback.textContent = error.message; }
  finally { setBusy(form, false); }
});

const resetToken = new URLSearchParams(location.search).get("resetToken");
if (resetToken) {
  document.getElementById("sign-in-view").hidden = true;
  document.getElementById("reset-view").hidden = false;
  document.getElementById("reset-form").hidden = true;
  document.querySelector("#reset-view .auth-intro").textContent = "Liên kết đã được xác nhận. Nhập email tài khoản và mật khẩu mới.";
  document.querySelector("#reset-view .reset-help").hidden = true;
  document.getElementById("new-password-form").hidden = false;
  document.getElementById("reset-account-email").focus();
}
document.getElementById("auth-year").textContent = new Date().getFullYear();
