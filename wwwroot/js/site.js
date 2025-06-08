// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

// Function to track link clicks
function trackLinkClick(uniqueLink) {
    fetch(`/Analytics/IncrementLinkClicks/${uniqueLink}`, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json'
        }
    }).catch(error => console.error('Error tracking link click:', error));
}

// Function to copy form link to clipboard and track the click
function copyToClipboard(elementId) {
    const element = document.getElementById(elementId);
    if (!element) return;

    // Copy to clipboard
    element.select();
    document.execCommand('copy');

    // Track the link click
    const uniqueLink = element.value;
    if (uniqueLink) {
        trackLinkClick(uniqueLink);
    }

    // Optional: Show feedback to user
    const button = element.nextElementSibling;
    const originalHtml = button.innerHTML;
    button.innerHTML = '<i class="bi bi-check2"></i>';
    setTimeout(() => {
        button.innerHTML = originalHtml;
    }, 2000);
}
