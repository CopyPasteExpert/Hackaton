console.log("SCRIPT.JS IS RUNNING!");

async function searchResults() {
    const participant = document.getElementById("participant").value;
    const competition = document.getElementById("competition").value;
    const year = document.getElementById("year").value;

    const params = new URLSearchParams();

    if (participant) params.append("participant", participant);
    if (competition) params.append("competition", competition);
    if (year) params.append("year", year);

    const response = await fetch(`/api/results?${params}`);

    const results = await response.json();
    const resultsDiv = document.getElementById("results");

    resultsDiv.innerHTML = "";

    if (results.length === 0) {
        resultsDiv.textContent = "No results found.";
        return;
    }

    results.forEach(result => {
        const element = document.createElement("div");

        element.innerHTML = `
            <h3>${result.participant}</h3>
            <p>Competition: ${result.competition}</p>
            <p>Year: ${result.year}</p>
            <p>Score: ${result.score ?? "Not available"}</p>
            <button onclick="saveResult(${result.id})">
                Save to my library
            </button>
        `;

        resultsDiv.appendChild(element);
    });
}

// REGISTER
document.getElementById("registerForm")
    .addEventListener("submit", async function (event) {
        console.log("REGISTER FORM SUBMITTED!");

        event.preventDefault();

        const email =
            document.getElementById("registerEmail").value;

        const password =
            document.getElementById("registerPassword").value;

        const message =
            document.getElementById("registerMessage");

        try {
            const response = await fetch("/api/account/register", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                credentials: "include",
                body: JSON.stringify({
                    email: email,
                    password: password
                })
            });

            if (response.ok) {
                message.textContent =
                    "Account created successfully! You can now log in.";
            } else {
                const error = await response.json();
                message.textContent =
                    error.title || JSON.stringify(error);
            }
        } catch {
            message.textContent = "Something went wrong.";
        }
    });


// LOGIN
document.getElementById("loginForm")
    .addEventListener("submit", async function (event) {
        console.log("LOGIN FORM SUBMITTED!");

        event.preventDefault();

        const email =
            document.getElementById("loginEmail").value;

        const password =
            document.getElementById("loginPassword").value;

        const message =
            document.getElementById("loginMessage");

        try {
            const response = await fetch(
                "/api/account/login?useCookies=true",
                {
                    method: "POST",
                    headers: {
                        "Content-Type": "application/json"
                    },
                    credentials: "include",
                    body: JSON.stringify({
                        email: email,
                        password: password
                    })
                }
            );

            if (response.ok) {
                message.textContent = "Successfully logged in!";

                await checkLoginStatus();
                console.log("Finished checking login status after login.");
            } else {
                const error = await response.json();
                message.textContent =
                    error.title || JSON.stringify(error);
            }
        } catch {
            message.textContent = "Something went wrong.";
        }
    });

async function checkLoginStatus() {
    const status = document.getElementById("accountStatus");
    const logoutButton = document.getElementById("logoutButton");

    try {
        const response = await fetch("/api/account/me", {
            credentials: "include"
        });

        if (response.ok) {
            const user = await response.json();

            status.textContent = `Logged in as ${user.email}`;
            logoutButton.hidden = false;
        } else if (response.status === 401) {
            status.textContent = "You are not logged in.";
            logoutButton.hidden = true;
        } else {
            status.textContent = "Could not check login status.";
            logoutButton.hidden = true;
        }

    } catch (error) {
        console.error("Login status error:", error);
        status.textContent = "Could not check login status.";
        logoutButton.hidden = true;
    }
}

document.getElementById("logoutButton")
    .addEventListener("click", async function () {

        const response = await fetch("/api/account/logout", {
            method: "POST",
            credentials: "include"
        });

        if (response.ok) {
            document.getElementById("loginMessage").textContent = "";

            await checkLoginStatus();

            console.log("Logged out successfully!");
        }
    });

console.log("ABOUT TO CHECK LOGIN STATUS");

checkLoginStatus();

console.log("CHECK LOGIN STATUS FUNCTION CALLED");

// SAVE A RESULT
async function saveResult(resultId) {
    const response = await fetch(`/api/saved-results/${resultId}`, {
        method: "POST",
        credentials: "include"
    });

    if (response.ok) {
        alert("Result saved!");
        loadLibrary();
    } else if (response.status === 409) {
        alert("You already saved this result!");
    } else if (response.status === 401) {
        alert("Please log in first!");
    } else {
        alert("Something went wrong.");
    }
}


// LOAD PERSONAL LIBRARY
async function loadLibrary() {
    const library = document.getElementById("library");

    const response = await fetch("/api/saved-results", {
        credentials: "include"
    });

    if (response.status === 401) {
        library.textContent = "Please log in to view your library.";
        return;
    }

    if (!response.ok) {
        library.textContent = "Could not load library.";
        return;
    }

    const results = await response.json();

    library.innerHTML = "";

    if (results.length === 0) {
        library.textContent = "Your library is empty.";
        return;
    }

    results.forEach(result => {
        const element = document.createElement("div");

        element.innerHTML = `
            <h3>${result.participant}</h3>
            <p>Competition: ${result.competition}</p>
            <p>Year: ${result.year}</p>
            <p>Score: ${result.score ?? "Not available"}</p>
            <button onclick="removeSavedResult(${result.id})">
                Remove
            </button>
        `;

        library.appendChild(element);
    });
}


// REMOVE A SAVED RESULT
async function removeSavedResult(savedId) {
    const response = await fetch(`/api/saved-results/${savedId}`, {
        method: "DELETE",
        credentials: "include"
    });

    if (response.ok) {
        loadLibrary();
    } else {
        alert("Could not remove result.");
    }
}