<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
  <!-- SAFE PLACEHOLDER. Stage-Local-DawiAssets.ps1 replaces this file only after
       the user's local female-dwarf TPACs and source skin have been verified. -->
  <xsl:output method="xml" indent="yes" />
  <xsl:template match="@*|node()">
    <xsl:copy><xsl:apply-templates select="@*|node()" /></xsl:copy>
  </xsl:template>
</xsl:stylesheet>
