(() => {
  const sidebar = document.getElementById("library-sidebar");
  const toggle = document.getElementById("sidebar-toggle");
  const scrim = document.getElementById("sidebar-scrim");
  const links = [...sidebar.querySelectorAll("[data-sidebar-view]")];
  if (window.innerWidth > 680) {
    const expanded = window.innerWidth > 1000;
    toggle.setAttribute("aria-expanded", String(expanded));
    toggle.setAttribute("aria-label", expanded ? "Thu gọn thanh điều hướng" : "Mở rộng thanh điều hướng");
  }

  function setOpen(open) {
    if (window.innerWidth <= 680) {
      document.body.classList.toggle("sidebar-open", open);
      toggle.setAttribute("aria-expanded", String(open));
      toggle.setAttribute("aria-label", open ? "Đóng thanh điều hướng" : "Mở thanh điều hướng");
      scrim.hidden = !open;
    } else {
      document.body.classList.remove("sidebar-open");
      document.body.classList.toggle("sidebar-collapsed", !open && window.innerWidth > 1000);
      document.body.classList.toggle("sidebar-expanded", open && window.innerWidth <= 1000);
      toggle.setAttribute("aria-expanded", String(open));
      toggle.setAttribute("aria-label", open ? "Mở rộng thanh điều hướng" : "Thu gọn thanh điều hướng");
      scrim.hidden = true;
    }
  }
  function activate(link) {
    links.forEach(item => item.classList.toggle("active", item === link));
    const view = link.dataset.sidebarView;
    if (view === "account") {
      if (document.getElementById("my-account").hidden) { window.location.assign("login.html"); return; }
      window.setFavoritesView(false);
      document.getElementById("my-account").scrollIntoView({ behavior: "smooth", block: "start" });
    } else if (view === "favorites") {
      window.setFavoritesView(true);
      document.getElementById("collection").scrollIntoView({ behavior: "smooth", block: "start" });
    } else if (view === "catalog") {
      window.setFavoritesView(false);
      document.getElementById("collection").scrollIntoView({ behavior: "smooth", block: "start" });
    } else if (["all", "Pending", "Reserved", "Borrowed", "history"].includes(view)) {
      window.setFavoritesView(false);
      window.filterLibraryRequests?.(view === "all" ? "all" : view);
      document.getElementById("my-requests").scrollIntoView({ behavior: "smooth", block: "start" });
    } else {
      window.setFavoritesView(false);
      window.scrollTo({ top: 0, behavior: "smooth" });
    }
    setOpen(false);
  }

  links.forEach(link => link.addEventListener("click", event => { event.preventDefault(); activate(link); }));
  toggle.addEventListener("click", () => setOpen(toggle.getAttribute("aria-expanded") !== "true"));
  scrim.addEventListener("click", () => setOpen(false));
  document.addEventListener("keydown", event => { if (event.key === "Escape") setOpen(false); });
  window.addEventListener("resize", () => {
    if (window.innerWidth > 680) {
      document.body.classList.remove("sidebar-open"); scrim.hidden = true;
      const expanded = !document.body.classList.contains("sidebar-collapsed");
      toggle.setAttribute("aria-expanded", String(expanded));
      toggle.setAttribute("aria-label", expanded ? "Thu gọn thanh điều hướng" : "Mở rộng thanh điều hướng");
    } else {
      document.body.classList.remove("sidebar-collapsed", "sidebar-expanded");
      toggle.setAttribute("aria-expanded", "false"); toggle.setAttribute("aria-label", "Mở thanh điều hướng"); scrim.hidden = true;
    }
  });
})();
