document.querySelectorAll("details").forEach((details) => {
  const content = details.querySelector<HTMLElement>(":scope > :not(summary)");
  if (!content) return;

  let animation: Animation | null = null;

  details.querySelector("summary")!.addEventListener("click", (e) => {
    e.preventDefault();
    animation?.cancel();

    if (details.open) {
      const startHeight = details.offsetHeight;
      const summaryHeight = details.querySelector("summary")!.offsetHeight;

      animation = details.animate(
        { height: [`${startHeight}px`, `${summaryHeight}px`] },
        { duration: 250, easing: "cubic-bezier(0.4, 0, 0.2, 1)" },
      );
      animation.onfinish = () => {
        details.open = false;
        animation = null;
      };
    } else {
      details.open = true;
      const endHeight = details.offsetHeight;
      const summaryHeight = details.querySelector("summary")!.offsetHeight;

      animation = details.animate(
        { height: [`${summaryHeight}px`, `${endHeight}px`] },
        { duration: 250, easing: "cubic-bezier(0.4, 0, 0.2, 1)" },
      );
      animation.onfinish = () => {
        details.style.height = "";
        animation = null;
      };
    }
  });
});
