<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
  <xsl:output method="xml" indent="yes" />

  <!-- Preserve the entire already-merged TOR skin registry by default. -->
  <xsl:template match="@*|node()">
    <xsl:copy>
      <xsl:apply-templates select="@*|node()" />
    </xsl:copy>
  </xsl:template>

  <!-- Replace only render/rig attributes on the existing adult dwarf woman.
       All TOR face/deform/voice child nodes are preserved. Male Dawi are untouched. -->
  <xsl:template match="race[@id='dwarf']/skin[@gender='1' and @name='woman' and @mesh_maturity_type='adult']">
    <xsl:copy>
      <xsl:apply-templates select="@*[not(name()='uses_stitching' or name()='body_mesh_suffix' or name()='min_scale' or name()='skeleton' or name()='body_meta_mesh' or name()='body_meta_mesh_shoulders' or name()='body_meta_mesh_upperbody' or name()='legs_mesh' or name()='hands_mesh' or name()='face_meta_mesh' or name()='underwear_bottom_mesh' or name()='underwear_top_mesh')]" />
      <xsl:attribute name="uses_stitching">false</xsl:attribute>
      <xsl:attribute name="body_mesh_suffix"></xsl:attribute>
      <xsl:attribute name="min_scale">1.06</xsl:attribute>
      <xsl:attribute name="skeleton">dwarf_skeleton_a</xsl:attribute>
      <xsl:attribute name="body_meta_mesh">sk_dwarf_bm_f1_body</xsl:attribute>
      <xsl:attribute name="body_meta_mesh_shoulders">sk_dwarf_bm_f1_shoulder</xsl:attribute>
      <xsl:attribute name="body_meta_mesh_upperbody">box_a</xsl:attribute>
      <xsl:attribute name="legs_mesh">sk_dwarf_bm_f1_legs</xsl:attribute>
      <xsl:attribute name="hands_mesh">sk_dwarf_bm_f1_arms</xsl:attribute>
      <xsl:attribute name="face_meta_mesh">sk_dwarf_bm_f1_head</xsl:attribute>
      <xsl:attribute name="underwear_bottom_mesh">sk_dwarf_underwear_female_a</xsl:attribute>
      <xsl:attribute name="underwear_top_mesh"></xsl:attribute>
      <xsl:apply-templates select="node()" />
    </xsl:copy>
  </xsl:template>
</xsl:stylesheet>
