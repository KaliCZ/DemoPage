<?xml version="1.0" encoding="UTF-8"?>
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:s="http://www.sitemaps.org/schemas/sitemap/0.9"
                xmlns:xhtml="http://www.w3.org/1999/xhtml">
  <xsl:output method="html" encoding="UTF-8" indent="yes"/>
  <xsl:template match="/">
    <html lang="en">
      <head>
        <meta charset="UTF-8"/>
        <title>Sitemap — kalandra.tech</title>
        <style>
          :root { color-scheme: light dark; }
          body { font-family: -apple-system, system-ui, sans-serif; max-width: 1100px; margin: 2rem auto; padding: 0 1rem; }
          h1 { font-size: 1.4rem; margin-bottom: 0.25rem; }
          p.meta { color: #666; margin-top: 0; }
          table { width: 100%; border-collapse: collapse; font-size: 0.9rem; }
          th, td { text-align: left; padding: 0.5rem 0.75rem; border-bottom: 1px solid #ddd; vertical-align: top; }
          th { background: rgba(127,127,127,0.1); font-weight: 600; }
          tr:hover td { background: rgba(127,127,127,0.05); }
          a { color: #2563eb; text-decoration: none; }
          a:hover { text-decoration: underline; }
          .alt { font-size: 0.8rem; color: #666; }
        </style>
      </head>
      <body>
        <h1>XML Sitemap</h1>
        <p class="meta">
          <xsl:value-of select="count(s:urlset/s:url)"/> URLs. This file is for search engines; the styling is just for human readability.
        </p>
        <table>
          <thead>
            <tr><th>URL</th><th>Last modified</th><th>Alternates</th></tr>
          </thead>
          <tbody>
            <xsl:for-each select="s:urlset/s:url">
              <tr>
                <td><a href="{s:loc}"><xsl:value-of select="s:loc"/></a></td>
                <td><xsl:value-of select="s:lastmod"/></td>
                <td class="alt">
                  <xsl:for-each select="xhtml:link">
                    <xsl:value-of select="@hreflang"/>: <a href="{@href}"><xsl:value-of select="@href"/></a><br/>
                  </xsl:for-each>
                </td>
              </tr>
            </xsl:for-each>
          </tbody>
        </table>
      </body>
    </html>
  </xsl:template>
</xsl:stylesheet>
