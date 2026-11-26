// Connect catalogue, session and reservation history to the distributed API.
(() => {
  const originalMakeRequest = window.makeRequest;
  const userSelect = document.getElementById("active-library-user");
  const userPicker = userSelect.closest(".request-user-picker");
  const bookIds = new Map();
  const statusText = { Pending: "Đang chờ xác nhận", Reserved: "Đã giữ sách", Rejected: "Không thể đáp ứng", Borrowed: "Đang mượn", Returned: "Đã trả sách", Cancelled: "Đã hủy" };
  let activeUserId = "";
  let apiReady = false;
  let requestStatusFilter = "all";

  async function api(path, options) {
    const response = await fetch(path, { credentials: "same-origin", ...options, headers: { "Content-Type": "application/json", ...(options?.headers || {}) } });
    if (!response.ok) {
      const body = await response.json().catch(() => ({}));
      throw new Error(body.error || (response.status === 401 ? "Vui lòng đăng nhập để thực hiện thao tác này." : `Yêu cầu thất bại (${response.status})`));
    }
    return response.status === 204 ? null : response.json();
  }

  function notify(message) {
    const toast = document.getElementById("toast");
    toast.textContent = message; toast.classList.add("show"); clearTimeout(notify.timer);
    notify.timer = setTimeout(() => toast.classList.remove("show"), 3000);
  }

  async function loadRequests() {
    if (!activeUserId) return;
    const rows = await api(`/users/${activeUserId}/reservations`);
    document.getElementById("request-count").textContent = rows.length;
    document.getElementById("pending-count").textContent = rows.filter(row => row.status === "Pending").length;
    document.getElementById("borrowed-count").textContent = rows.filter(row => row.status === "Borrowed").length;
    const visibleRows = rows.filter(row => requestStatusFilter === "all" || (requestStatusFilter === "history" ? ["Returned", "Rejected"].includes(row.status) : row.status === requestStatusFilter));
    const host = document.getElementById("request-list");
    host.innerHTML = visibleRows.length ? visibleRows.map(row => {
      const action = ["Pending", "Reserved"].includes(row.status) ? `<button class="request-action secondary" type="button" data-transition="cancel" data-id="${escapeHTML(row.id)}">Hủy đặt sách</button>`
        : `<span class="request-date">${row.status === "Pending" ? "Đang chờ thư viện xử lý" : "Không cần thao tác"}</span>`;
      return `<div class="request-row"><div><span class="request-book">${escapeHTML(row.bookTitle)}</span><span class="request-sub">Mã ${escapeHTML(row.id)}</span></div><span class="request-date">${escapeHTML(new Intl.DateTimeFormat("vi-VN").format(new Date(row.createdAt)))}</span><span class="status ${row.status.toLowerCase()}">${statusText[row.status] || row.status}</span><span class="request-date">Cập nhật từ hệ thống</span>${action}</div>`;
    }).join("") : rows.length ? '<div class="request-empty">Không có yêu cầu nào trong mục này.</div>' : '<div class="request-empty">Bạn chưa có yêu cầu nào. Hãy chọn một cuốn sách để bắt đầu nhé.</div>';
  }
  window.filterLibraryRequests = filter => { requestStatusFilter = filter; loadRequests().catch(error => notify(error.message)); };

  document.getElementById("request-list").addEventListener("click", async event => {
    const button = event.target.closest("[data-transition]");
    if (!button) return;
    button.disabled = true;
    try {
      await api(`/reservations/${button.dataset.id}/${button.dataset.transition}`, { method: "POST" });
      await Promise.all([loadRequests(), refreshBooks()]);
      notify("Đã cập nhật trạng thái yêu cầu.");
    } catch (error) { notify(error.message); button.disabled = false; }
  });

  async function refreshBooks() {
    const items = await api("/books");
    if (!items.length) return;
    document.getElementById("hero-book-count").textContent = `${items.length} đầu sách`;
    document.querySelector('.category[data-category="Tất cả"] span').textContent = items.length;
    bookIds.clear();
    books.splice(0, books.length, ...items.map((item, index) => {
      const id = index + 1;
      bookIds.set(String(id), item.id);
      return { id, title: item.title, author: item.author, category: item.category, year: item.publishedYear,
        copies: item.availableCopies, total: item.totalCopies, cover: Number(item.coverKey) || id, tag: item.tag, description: item.description };
    }));
    renderBooks();
  }

  window.makeRequest = async function (id) {
    if (!apiReady) return originalMakeRequest(id);
    if (!activeUserId) { window.location.assign("login.html"); return; }
    const bookId = bookIds.get(String(id));
    if (!bookId) { notify("Không tìm thấy mã sách trên máy chủ."); return; }
    try {
      await api("/reservations", { method: "POST", body: JSON.stringify({ userId: activeUserId, bookId }) });
      if (dialog.open) dialog.close();
      notify("Đã gửi yêu cầu. Thư viện đang kiểm tra số bản còn.");
      await loadRequests();
      setTimeout(() => loadRequests().catch(() => {}), 1200);
      document.getElementById("my-requests").scrollIntoView({ behavior: "smooth", block: "start" });
    } catch (error) { notify(error.message); }
  };

  async function connect() {
    try {
      await refreshBooks();
      const sessionResponse = await fetch("/auth/me", { credentials: "same-origin" });
      if (sessionResponse.ok) {
        const session = await sessionResponse.json();
        activeUserId = session.id;
        document.querySelectorAll(".staff-nav").forEach(node => node.hidden = !["Librarian", "Admin"].includes(session.role));
        document.querySelectorAll(".admin-nav").forEach(node => node.hidden = session.role !== "Admin");
        localStorage.setItem("library-user-id", activeUserId);
        userPicker.hidden = true;
        const accountSection = document.getElementById("my-account");
        accountSection.hidden = false;
        document.getElementById("account-avatar").textContent = session.name.trim().charAt(0).toUpperCase();
        document.getElementById("account-name").textContent = session.name;
        document.getElementById("account-email").textContent = session.email;
        document.getElementById("account-phone").textContent = session.phone || "Chưa cập nhật";
        const profile = document.querySelector(".profile-button");
        profile.href = "#my-requests";
        profile.querySelector(".profile-name").textContent = session.name;
        profile.querySelector(".avatar").textContent = session.name.trim().charAt(0).toUpperCase();
        profile.setAttribute("aria-label", `Đăng xuất ${session.name}`);
        const account = document.querySelector(".sidebar-account");
        account.href = "#my-account";
        account.querySelector(".avatar").textContent = session.name.trim().charAt(0).toUpperCase();
        document.getElementById("sidebar-account-caption").textContent = session.email;
        const logout = async () => {
          try { await api("/auth/logout", { method: "POST" }); } finally { localStorage.removeItem("library-user-id"); location.assign("index.html"); }
        };
        profile.addEventListener("click", async event => {
          event.preventDefault();
          await logout();
        });
        document.getElementById("account-logout").addEventListener("click", logout);
        await loadRequests();
        setInterval(() => loadRequests().catch(() => {}), 3000);
      } else {
        activeUserId = "";
        localStorage.removeItem("library-user-id");
        userPicker.hidden = true;
        document.getElementById("request-list").innerHTML = '<div class="request-empty">Đăng nhập để gửi yêu cầu và theo dõi sách đang mượn. <a href="login.html">Đăng nhập</a></div>';
      }
      apiReady = true;
      setInterval(() => refreshBooks().catch(() => {}), 10000);
    } catch {
      userPicker.hidden = true;
      // Keep the static catalogue available if the page is opened without its API.
    }
  }
  connect();
})();
