window.statmasterAuth = {
  async login(username, password) {
    try {
      const response = await fetch("/auth/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ username, password })
      });

      if (!response.ok) {
        return { ok: false, message: "Invalid username or password." };
      }

      const data = await response.json();
      return { ok: true, mustChangePassword: !!data.mustChangePassword };
    } catch {
      return { ok: false, message: "Login failed." };
    }
  },

  async changePassword(oldPassword, newPassword, confirmPassword) {
    try {
      const response = await fetch("/auth/change-password", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ oldPassword, newPassword, confirmPassword })
      });

      const body = await response.json().catch(() => ({}));
      if (!response.ok) {
        return { ok: false, message: body.message || "Password change failed." };
      }

      return { ok: true, message: body.message || "Password changed." };
    } catch {
      return { ok: false, message: "Password change failed." };
    }
  },

  async logout() {
    await fetch("/auth/logout", { method: "POST" });
  }
};
