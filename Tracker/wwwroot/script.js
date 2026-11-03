// Every piece of data from the server is put into the page with textContent,
// never innerHTML, so names in the database cannot inject markup.

const $ = (id) => document.getElementById(id);

function el(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined && text !== null) node.textContent = text;
    return node;
}

let toastTimer;

function toast(text) {
    const box = $("toast");
    box.textContent = text;
    box.hidden = false;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => { box.hidden = true; }, 2600);
}

function initials(name) {
    return name.split(/\s+/).filter(Boolean).slice(0, 2)
        .map(part => part[0].toUpperCase()).join("");
}

function formatScore(score) {
    return score === null || score === undefined
        ? null
        : Number(score).toLocaleString("bg-BG", { maximumFractionDigits: 2 });
}

function competitionsLabel(count) {
    return count === 1 ? "1 състезание" : `${count} състезания`;
}

// One competition result as a row. `action` is an optional button.
function resultRow(result, action) {
    const row = el("div", "row");

    row.append(el("span", "year", result.year));

    const text = el("div", "row-text");
    text.append(el("div", "row-title", result.competition));
    if (result.participant) {
        text.append(el("div", "row-sub", result.participant));
    }

    // "1 място · 11 клас · Протокол (МОН)", with only the parts we have.
    const details = [];
    if (result.place) details.push(el("span", "place", `${result.place} място`));
    if (result.grade) details.push(el("span", null, `${result.grade} клас`));
    if (typeof result.sourceUrl === "string" && result.sourceUrl.startsWith("https://")) {
        const link = el("a", "source", "Протокол (МОН)");
        link.href = result.sourceUrl;
        link.target = "_blank";
        link.rel = "noopener";
        details.push(link);
    }

    if (details.length > 0) {
        const line = el("div", "row-sub row-details");
        details.forEach((part, i) => {
            if (i > 0) line.append(" · ");
            line.append(part);
        });
        text.append(line);
    }

    row.append(text);

    const score = formatScore(result.score);
    row.append(score === null
        ? el("span", "score none", "без точки")
        : el("span", "score", score));

    if (action) {
        row.append(action);
    }

    return row;
}

function saveButton(resultId) {
    const button = el("button", "button ghost small", "Запази");
    button.type = "button";
    button.addEventListener("click", () => saveResult(resultId, button));
    return button;
}

// SMART SEARCH

async function smartSearch(query) {
    const status = $("searchStatus");
    const results = $("results");

    query = query.trim();
    if (!query) return;

    $("searchInput").value = query;
    status.replaceChildren(el("div", "spinner"));
    results.replaceChildren();

    let data;

    try {
        const response = await fetch(`/api/search?q=${encodeURIComponent(query)}`);

        if (response.status === 400) {
            status.replaceChildren(el("p", "message", "Напиши име, за да търсиш."));
            return;
        }

        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        data = await response.json();
    } catch (error) {
        console.error("Search failed:", error);
        status.replaceChildren(el("p", "message error",
            "Търсенето не успя. Провери дали сървърът и базата работят."));
        return;
    }

    status.replaceChildren();

    if (data.aiNote) {
        status.append(el("div", "ai-note", `🤖 ${data.aiNote}`));
    }

    if (data.students.length === 0) {
        const message = el("p", "message", "Не намерихме ученик с това име.");

        if (data.didYouMean) {
            message.append(" Имахте предвид ");
            const suggestion = el("button", "did-you-mean", data.didYouMean);
            suggestion.type = "button";
            suggestion.addEventListener("click", () => smartSearch(data.didYouMean));
            message.append(suggestion, "?");
        }

        status.append(message);
        return;
    }

    results.replaceChildren(...data.students.map(studentCard));
}

function studentCard(student) {
    const card = el("article", "card");

    const head = el("div", "card-head");
    head.append(el("div", "avatar", initials(student.name)));

    const title = el("div", "card-title");
    title.append(el("h3", null, student.name));
    title.append(el("div", "card-meta", competitionsLabel(student.results.length)));
    head.append(title);

    head.append(el("span", "match",
        `${Math.round(student.matchScore * 100)}% съвпадение`));

    const rows = el("div", "rows");
    rows.append(...student.results.map(r => resultRow(r, saveButton(r.id))));

    card.append(head, rows);
    return card;
}

$("searchForm").addEventListener("submit", (event) => {
    event.preventDefault();
    smartSearch($("searchInput").value);
});

document.querySelectorAll("[data-example]").forEach(chip => {
    chip.addEventListener("click", () => smartSearch(chip.dataset.example));
});

// ADVANCED SEARCH (the original /api/results filters)

$("advancedForm").addEventListener("submit", async (event) => {
    event.preventDefault();

    const params = new URLSearchParams();
    const participant = $("participant").value.trim();
    const competition = $("competition").value.trim();
    const year = $("year").value.trim();

    if (participant) params.append("participant", participant);
    if (competition) params.append("competition", competition);
    if (year) params.append("year", year);

    const box = $("advancedResults");
    box.replaceChildren(el("div", "spinner"));

    try {
        const response = await fetch(`/api/results?${params}`);
        if (!response.ok) throw new Error(`HTTP ${response.status}`);

        const results = await response.json();

        box.replaceChildren(...(results.length === 0
            ? [el("p", "message", "Няма резултати.")]
            : results.map(r => resultRow(r, saveButton(r.id)))));
    } catch (error) {
        console.error("Advanced search failed:", error);
        box.replaceChildren(el("p", "message error", "Търсенето не успя."));
    }
});

// ACCOUNT

const dialog = $("accountDialog");

function showTab(name) {
    document.querySelectorAll(".tab").forEach(tab =>
        tab.classList.toggle("active", tab.dataset.tab === name));
    document.querySelectorAll("[data-panel]").forEach(panel =>
        panel.hidden = panel.dataset.panel !== name);
}

document.querySelectorAll(".tab").forEach(tab =>
    tab.addEventListener("click", () => showTab(tab.dataset.tab)));

$("accountButton").addEventListener("click", () => {
    showTab("login");
    dialog.showModal();
});

function setMessage(id, text, isError) {
    const message = $(id);
    message.textContent = text;
    message.classList.toggle("error", Boolean(isError));
}

// Identity endpoints answer with a ProblemDetails body; turn it into one line.
async function errorText(response) {
    try {
        const problem = await response.json();
        if (problem.errors) {
            return Object.values(problem.errors).flat().join(" ");
        }
        return problem.detail || problem.title || "Нещо се обърка.";
    } catch {
        return "Нещо се обърка.";
    }
}

$("registerForm").addEventListener("submit", async (event) => {
    event.preventDefault();

    try {
        const response = await fetch("/api/account/register", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            credentials: "include",
            body: JSON.stringify({
                email: $("registerEmail").value,
                password: $("registerPassword").value
            })
        });

        if (response.ok) {
            setMessage("registerMessage", "Акаунтът е създаден. Сега влез.");
            $("loginEmail").value = $("registerEmail").value;
            showTab("login");
        } else {
            setMessage("registerMessage", await errorText(response), true);
        }
    } catch {
        setMessage("registerMessage", "Нещо се обърка.", true);
    }
});

$("loginForm").addEventListener("submit", async (event) => {
    event.preventDefault();

    try {
        const response = await fetch("/api/account/login?useCookies=true", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            credentials: "include",
            body: JSON.stringify({
                email: $("loginEmail").value,
                password: $("loginPassword").value
            })
        });

        if (response.ok) {
            setMessage("loginMessage", "");
            $("loginPassword").value = "";
            dialog.close();
            await checkLoginStatus();
            toast("Влезе успешно.");
        } else {
            setMessage("loginMessage",
                response.status === 401 ? "Грешен имейл или парола." : await errorText(response),
                true);
        }
    } catch {
        setMessage("loginMessage", "Нещо се обърка.", true);
    }
});

let loggedIn = false;

async function checkLoginStatus() {
    try {
        const response = await fetch("/api/account/me", { credentials: "include" });
        loggedIn = response.ok;

        $("accountStatus").textContent = loggedIn ? (await response.json()).email : "";
    } catch (error) {
        console.error("Login status error:", error);
        loggedIn = false;
        $("accountStatus").textContent = "";
    }

    $("accountButton").hidden = loggedIn;
    $("logoutButton").hidden = !loggedIn;
    $("libraryButton").hidden = !loggedIn;
    $("librarySection").hidden = !loggedIn;

    if (loggedIn) {
        loadLibrary();
    }
}

$("logoutButton").addEventListener("click", async () => {
    const response = await fetch("/api/account/logout", {
        method: "POST",
        credentials: "include"
    });

    if (response.ok) {
        await checkLoginStatus();
        toast("Излезе от акаунта.");
    }
});

$("libraryButton").addEventListener("click", () =>
    $("librarySection").scrollIntoView({ behavior: "smooth" }));

// LIBRARY

async function saveResult(resultId, button) {
    if (!loggedIn) {
        showTab("login");
        setMessage("loginMessage", "Влез, за да запазваш резултати.");
        dialog.showModal();
        return;
    }

    button.disabled = true;

    const response = await fetch(`/api/saved-results/${resultId}`, {
        method: "POST",
        credentials: "include"
    });

    if (response.ok) {
        button.textContent = "Запазено ✓";
        toast("Добавено в библиотеката.");
        loadLibrary();
    } else if (response.status === 409) {
        button.textContent = "Запазено ✓";
        toast("Вече е в библиотеката ти.");
    } else {
        button.disabled = false;
        toast("Не успяхме да запазим резултата.");
    }
}

async function loadLibrary() {
    const library = $("library");

    const response = await fetch("/api/saved-results", { credentials: "include" });

    if (!response.ok) {
        library.replaceChildren(el("p", "message error", "Библиотеката не се зареди."));
        return;
    }

    const results = await response.json();

    if (results.length === 0) {
        library.replaceChildren(el("p", "message",
            "Още нямаш запазени резултати. Натисни „Запази“ до някой резултат."));
        return;
    }

    library.replaceChildren(...results.map(result => {
        const remove = el("button", "button ghost small", "Премахни");
        remove.type = "button";
        remove.addEventListener("click", () => removeSavedResult(result.id));
        return resultRow(result, remove);
    }));
}

async function removeSavedResult(savedId) {
    const response = await fetch(`/api/saved-results/${savedId}`, {
        method: "DELETE",
        credentials: "include"
    });

    if (response.ok) {
        loadLibrary();
    } else {
        toast("Не успяхме да премахнем резултата.");
    }
}

$("refreshLibraryButton").addEventListener("click", loadLibrary);

checkLoginStatus();
