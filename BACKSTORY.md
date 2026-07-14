# Behind the scenes

As the README says, this software exists because I wanted to preserve the data I had in Readerware. But there are some other considerations.

I have always been an avid reader and developer, so I built my first book database application in '92–93. That was done using Delphi and QuickReports, as I started programming with a TP 3.0 clone on a locally designed computer and was a Delphi person for many years. I managed to have exactly *one* cover in the database — the paperback edition of *To Green Angel Tower*, scanned on an early Mac at university.

That software required maintenance and eventually fell afoul of Delphi 8, so I ended up keeping a book list in a plain text file.

Some years later I discovered Readerware with its auto-catalog feature, which finally let me maintain a reasonably complete database of my books.

Fast-forwarding to more recently: I, along with many others, noticed something was wrong with Readerware, figured out what had happened — the developer had passed away — and started looking for alternatives. Those are hard to find in a niche like this, especially if you refuse browser or cloud based offerings. While looking, I had an idea: why not use this as a test case for trying out coding agents, spec-driven development, and that kind of thing? I do use those approaches at work, but not in this from-scratch manner. The idea was that it should be possible to create something to keep my database alive with a reasonable amount of manual effort.

So, a Claude subscription was purchased (including an overage budget), GSD was installed, and away we went — although the final trim for version 1 was done using Opus 4.8 in straight-up mode. Versions 1.1 and later have been built using a somewhat more lightweight custom process and skill, in part using Fable 5 but mostly still Opus 4.8.

With that said — no, I have not read every single line in this codebase. I have run tests against the data I have. There are some visual issues I am not happy with, but it should do its intended job.

The translations probably leave something to be desired. It all sounded good in the specs with vocabulary management and terminology control, but do I trust the result? Not entirely.

---

If you find this useful, you are welcome to use the links below for support. It might help offset the subscription and overage costs.

[![Donate with PayPal](https://www.paypalobjects.com/en_US/i/btn/btn_donate_LG.gif)](https://www.paypal.com/donate/?business=TGHM3KS7ZG7TA&no_recurring=1&item_name=Support+BookDB&currency_code=SEK) · [![Ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/Z7V2203Z3A)
